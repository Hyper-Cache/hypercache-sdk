// Package hypercache provides a client for the HyperCache API.
//
// Quickstart:
//
//	import (
//		"context"
//		"fmt"
//		"hypercache"
//	)
//
//	client, err := hypercache.NewClient()  // reads HYPERCACHE_KEY from env
//	if err != nil { panic(err) }
//
//	result, err := client.Fingerprint(context.Background(), data)
//	if err != nil {
//		if hypercache.IsQuotaError(err) {
//			// handle quota exhaustion
//		}
//		panic(err)
//	}
//	fmt.Println(result.RecordHex, result.OpsRemaining)
//
// Audit chain (records linked to a prior record):
//
//	r1, _ := client.Fingerprint(ctx, batch1)
//	r2, _ := client.Fingerprint(ctx, batch2, hypercache.WithPrev(r1.Record))
package hypercache

import (
	"bytes"
	"context"
	stdBase64 "encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"strconv"
	"strings"
	"time"
)

// Version of the SDK.
const Version = "0.1.0"

const (
	defaultBaseURL = "https://api.hypercache.ai"
	defaultLayers  = 32
	defaultNTok    = 64
	defaultTimeout = 30 * time.Second
)

// APIError is returned by Client.Fingerprint when the server responds with a
// non-2xx status, or when the request fails before reaching the server.
// Status is 0 for network/build failures, otherwise the HTTP status code.
type APIError struct {
	Status  int
	Message string
}

func (e *APIError) Error() string {
	if e.Status == 0 {
		return "hypercache: " + e.Message
	}
	return fmt.Sprintf("hypercache: HTTP %d: %s", e.Status, e.Message)
}

// Status-based error predicates. Use these instead of inspecting *APIError directly
// so future changes to the error type don't break caller code.

// IsAuthError reports whether err is an APIError with status 401.
func IsAuthError(err error) bool { return errStatus(err) == 401 }

// IsQuotaError reports whether err is an APIError with status 402.
func IsQuotaError(err error) bool { return errStatus(err) == 402 }

// IsRateLimitError reports whether err is an APIError with status 429.
func IsRateLimitError(err error) bool { return errStatus(err) == 429 }

// IsClientError reports whether err is an APIError with status 4xx.
func IsClientError(err error) bool {
	s := errStatus(err)
	return s >= 400 && s < 500
}

// IsServerError reports whether err is an APIError with status 5xx or
// a transient network failure (Status == 0).
func IsServerError(err error) bool {
	s := errStatus(err)
	return s == 0 || s >= 500
}

func errStatus(err error) int {
	var e *APIError
	if errors.As(err, &e) {
		return e.Status
	}
	return -1
}

// Result is the parsed response from /v1/fingerprint.
type Result struct {
	Record    []byte // raw record
	RecordHex string // hex string
	Version   int    // record format version (currently 2)

	// Quota headers — -1 means the header was missing (shouldn't happen on success).
	OpsUsed      int
	OpsCap       int
	OpsRemaining int
}

// Client makes calls to the Hyper Cache API.
type Client struct {
	apiKey     string
	baseURL    string
	httpClient *http.Client
}

// Option configures a Client.
type Option func(*Client) error

// WithAPIKey sets the API key explicitly. Without this, NewClient reads HYPERCACHE_KEY from os.Getenv.
func WithAPIKey(key string) Option {
	return func(c *Client) error {
		c.apiKey = key
		return nil
	}
}

// WithBaseURL overrides the default API base URL.
func WithBaseURL(url string) Option {
	return func(c *Client) error {
		c.baseURL = strings.TrimRight(url, "/")
		return nil
	}
}

// WithTimeout sets the HTTP request timeout (default 30s).
func WithTimeout(d time.Duration) Option {
	return func(c *Client) error {
		c.httpClient.Timeout = d
		return nil
	}
}

// WithHTTPClient supplies a custom *http.Client (e.g. to share connection pools
// or inject a transport for tracing).
func WithHTTPClient(h *http.Client) Option {
	return func(c *Client) error {
		c.httpClient = h
		return nil
	}
}

// NewClient builds a Client. Without options, the API key is taken from HYPERCACHE_KEY
// and the base URL from HYPERCACHE_BASE_URL (or the production default,
// https://api.hypercache.ai, if unset).
func NewClient(opts ...Option) (*Client, error) {
	c := &Client{
		apiKey:     os.Getenv("HYPERCACHE_KEY"),
		baseURL:    defaultBaseURL,
		httpClient: &http.Client{Timeout: defaultTimeout},
	}
	if env := os.Getenv("HYPERCACHE_BASE_URL"); env != "" {
		c.baseURL = strings.TrimRight(env, "/")
	}
	for _, opt := range opts {
		if err := opt(c); err != nil {
			return nil, err
		}
	}
	if c.apiKey == "" {
		return nil, &APIError{
			Status:  401,
			Message: "no API key: use WithAPIKey() or set HYPERCACHE_KEY in your environment",
		}
	}
	return c, nil
}

// FingerprintOption configures a single Fingerprint call.
type FingerprintOption func(*fingerprintOpts)

type fingerprintOpts struct {
	layers int
	nTok   int
	prev   []byte
}

// WithLayers sets the model-layer-count hint header (default 32).
func WithLayers(n int) FingerprintOption {
	return func(o *fingerprintOpts) { o.layers = n }
}

// WithNTok sets the token-count hint header (default 64).
func WithNTok(n int) FingerprintOption {
	return func(o *fingerprintOpts) { o.nTok = n }
}

// WithPrev links this call's record to a prior record.
func WithPrev(prev []byte) FingerprintOption {
	return func(o *fingerprintOpts) { o.prev = prev }
}

// Fingerprint computes a fingerprint for the given data.
func (c *Client) Fingerprint(ctx context.Context, data []byte, opts ...FingerprintOption) (*Result, error) {
	o := &fingerprintOpts{layers: defaultLayers, nTok: defaultNTok}
	for _, opt := range opts {
		opt(o)
	}

	req, err := http.NewRequestWithContext(
		ctx, http.MethodPost, c.baseURL+"/v1/fingerprint",
		bytes.NewReader(data),
	)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "build request: " + err.Error()}
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("Content-Type", "application/octet-stream")
	req.Header.Set("X-Hc-Layers", strconv.Itoa(o.layers))
	req.Header.Set("X-Hc-N-Tok", strconv.Itoa(o.nTok))
	req.Header.Set("User-Agent", "hypercache-go/"+Version)
	if len(o.prev) > 0 {
		req.Header.Set("X-Hc-Prev", hex.EncodeToString(o.prev))
	}

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "request failed: " + err.Error()}
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, &APIError{Status: resp.StatusCode, Message: "read body: " + err.Error()}
	}

	if resp.StatusCode != http.StatusOK {
		return nil, &APIError{
			Status:  resp.StatusCode,
			Message: strings.TrimSpace(string(body)),
		}
	}

	var payload struct {
		FingerprintHex string `json:"fingerprint_hex"`
		Version        int    `json:"version"`
	}
	if err := json.Unmarshal(body, &payload); err != nil {
		return nil, &APIError{Status: 0, Message: "parse response: " + err.Error()}
	}

	record, err := hex.DecodeString(payload.FingerprintHex)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "decode fingerprint hex: " + err.Error()}
	}

	return &Result{
		Record:       record,
		RecordHex:    payload.FingerprintHex,
		Version:      payload.Version,
		OpsUsed:      headerInt(resp.Header, "X-Hc-Ops-Used"),
		OpsCap:       headerInt(resp.Header, "X-Hc-Ops-Cap"),
		OpsRemaining: headerInt(resp.Header, "X-Hc-Ops-Remaining"),
	}, nil
}

func headerInt(h http.Header, key string) int {
	s := h.Get(key)
	if s == "" {
		return -1
	}
	n, err := strconv.Atoi(s)
	if err != nil {
		return -1
	}
	return n
}

// ---------- Cache methods ----------

// CachePutResult is the response from CachePut.
type CachePutResult struct {
	SizeBytes int64 `json:"size_bytes"`
	// ExpiresAt is the unix epoch second at which the entry expires.
	// Zero means stored with no expiry.
	ExpiresAt    int64
	// Label is the optional organizer string sent via x-hc-label (echoed back).
	Label        string
	// Run is the optional run/session identifier sent via x-hc-run (echoed back).
	Run          string
	OpsUsed      int // -1 if header missing
	OpsCap       int
	OpsRemaining int
}

// CachePutOption configures a single CachePut call.
type CachePutOption func(*cachePutOpts)

type cachePutOpts struct {
	ttl    int
	ttlSet bool
	label  string
	run    string
}

// WithTTL sets the entry's expiry in seconds. 0 = no expiry. Omit to use tier default.
func WithTTL(ttlSeconds int) CachePutOption {
	return func(o *cachePutOpts) {
		o.ttl = ttlSeconds
		o.ttlSet = true
	}
}

// WithLabel attaches an organizer string to the cache entry.
// Max 256 ASCII printable chars. Do NOT put PHI or secrets in labels —
// they're stored as plaintext metadata in D1 and visible in the dashboard.
func WithLabel(label string) CachePutOption {
	return func(o *cachePutOpts) { o.label = label }
}

// WithRun attaches a run/session identifier to the cache entry.
// Use for grouping related entries; query via Client.CacheList with a Run filter.
// Max 256 ASCII printable chars.
func WithRun(run string) CachePutOption {
	return func(o *cachePutOpts) { o.run = run }
}

// CachePut stores data under the given fingerprint.
//
// On success returns CachePutResult with size + expiry + updated quota counters.
// Returns IsQuotaError on 402 (op cap reached or cache quota exceeded) and
// IsClientError on 400/413 (bad fingerprint, empty body, object too large).
func (c *Client) CachePut(ctx context.Context, fingerprint string, data []byte, opts ...CachePutOption) (*CachePutResult, error) {
	o := &cachePutOpts{}
	for _, opt := range opts {
		opt(o)
	}

	req, err := http.NewRequestWithContext(
		ctx, http.MethodPut, c.baseURL+"/v1/cache/"+fingerprint,
		bytes.NewReader(data),
	)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "build request: " + err.Error()}
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("Content-Type", "application/octet-stream")
	req.Header.Set("User-Agent", "hypercache-go/"+Version)
	if o.ttlSet {
		req.Header.Set("X-Hc-TTL", strconv.Itoa(o.ttl))
	}
	if o.label != "" {
		req.Header.Set("X-Hc-Label", o.label)
	}
	if o.run != "" {
		req.Header.Set("X-Hc-Run", o.run)
	}

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "request failed: " + err.Error()}
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, &APIError{Status: resp.StatusCode, Message: "read body: " + err.Error()}
	}

	if resp.StatusCode != http.StatusOK {
		return nil, &APIError{Status: resp.StatusCode, Message: strings.TrimSpace(string(body))}
	}

	var payload struct {
		Stored    bool    `json:"stored"`
		SizeBytes int64   `json:"size_bytes"`
		ExpiresAt *int64  `json:"expires_at"`
		Label     *string `json:"label"`
		Run       *string `json:"run"`
	}
	if err := json.Unmarshal(body, &payload); err != nil {
		return nil, &APIError{Status: 0, Message: "parse response: " + err.Error()}
	}

	expiresAt := int64(0)
	if payload.ExpiresAt != nil {
		expiresAt = *payload.ExpiresAt
	}
	label := ""
	if payload.Label != nil {
		label = *payload.Label
	}
	run := ""
	if payload.Run != nil {
		run = *payload.Run
	}

	return &CachePutResult{
		SizeBytes:    payload.SizeBytes,
		ExpiresAt:    expiresAt,
		Label:        label,
		Run:          run,
		OpsUsed:      headerInt(resp.Header, "X-Hc-Ops-Used"),
		OpsCap:       headerInt(resp.Header, "X-Hc-Ops-Cap"),
		OpsRemaining: headerInt(resp.Header, "X-Hc-Ops-Remaining"),
	}, nil
}

// CacheGet retrieves cached bytes for the given fingerprint.
//
// Returns (nil, nil) on cache miss (404 is the expected miss case, not an error).
// Returns (nil, error) on other HTTP failures.
func (c *Client) CacheGet(ctx context.Context, fingerprint string) ([]byte, error) {
	req, err := http.NewRequestWithContext(
		ctx, http.MethodGet, c.baseURL+"/v1/cache/"+fingerprint, nil,
	)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "build request: " + err.Error()}
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("User-Agent", "hypercache-go/"+Version)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "request failed: " + err.Error()}
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusNotFound {
		return nil, nil // cache miss is not an error
	}

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, &APIError{Status: resp.StatusCode, Message: "read body: " + err.Error()}
	}

	if resp.StatusCode != http.StatusOK {
		return nil, &APIError{Status: resp.StatusCode, Message: strings.TrimSpace(string(body))}
	}
	return body, nil
}

// CacheDelete deletes the cached entry. Idempotent — does not error on already-deleted.
func (c *Client) CacheDelete(ctx context.Context, fingerprint string) error {
	req, err := http.NewRequestWithContext(
		ctx, http.MethodDelete, c.baseURL+"/v1/cache/"+fingerprint, nil,
	)
	if err != nil {
		return &APIError{Status: 0, Message: "build request: " + err.Error()}
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("User-Agent", "hypercache-go/"+Version)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return &APIError{Status: 0, Message: "request failed: " + err.Error()}
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(resp.Body)
		return &APIError{Status: resp.StatusCode, Message: strings.TrimSpace(string(body))}
	}
	return nil
}

// BatchLookupItem is one item in a CacheLookupBatch result.
type BatchLookupItem struct {
	Hit            bool   `json:"hit"`
	FingerprintHex string `json:"fingerprint_hex"`
	Value          []byte // decoded from value_b64 on hit; nil on miss
	Expired        bool   `json:"expired,omitempty"`
	SizeBytes      int64  `json:"size_bytes,omitempty"`
	StoredAt       int64  `json:"stored_at,omitempty"`
	ExpiresAt      int64  `json:"expires_at,omitempty"`
}

// BatchInput is one input to CacheLookupBatch. Either pass raw bytes via
// Data, or use BatchInputs for many in one round trip.
type BatchInput struct {
	Data   []byte
	Prev   []byte // optional prior record for chain linkage
	Layers int    // optional override; 0 means use default
	NTok   int    // optional override; 0 means use default
}

// CacheLookupBatch fingerprints and cache-checks many inputs in one round trip.
// Op accounting is strict all-or-nothing: if the batch would exceed your op cap,
// nothing is charged and a quota error is returned with current quota info.
func (c *Client) CacheLookupBatch(ctx context.Context, inputs []BatchInput) ([]BatchLookupItem, error) {
	if len(inputs) == 0 {
		return nil, &APIError{Status: 0, Message: "CacheLookupBatch: empty inputs"}
	}

	type itemPayload struct {
		DataB64 string `json:"data_b64"`
		Layers  int    `json:"layers,omitempty"`
		NTok    int    `json:"n_tok,omitempty"`
		PrevHex string `json:"prev_hex,omitempty"`
	}
	payload := struct {
		Items []itemPayload `json:"items"`
	}{Items: make([]itemPayload, 0, len(inputs))}

	for i, in := range inputs {
		if len(in.Data) == 0 {
			return nil, &APIError{Status: 0, Message: fmt.Sprintf("CacheLookupBatch: inputs[%d].Data is empty", i)}
		}
		item := itemPayload{
			DataB64: base64Encode(in.Data),
		}
		if in.Layers > 0 {
			item.Layers = in.Layers
		}
		if in.NTok > 0 {
			item.NTok = in.NTok
		}
		if len(in.Prev) > 0 {
			item.PrevHex = hex.EncodeToString(in.Prev)
		}
		payload.Items = append(payload.Items, item)
	}

	body, err := json.Marshal(payload)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "marshal: " + err.Error()}
	}

	req, err := http.NewRequestWithContext(
		ctx, http.MethodPost, c.baseURL+"/v1/cache/lookup/batch", bytes.NewReader(body),
	)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "build request: " + err.Error()}
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", "hypercache-go/"+Version)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "request failed: " + err.Error()}
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		b, _ := io.ReadAll(resp.Body)
		return nil, &APIError{Status: resp.StatusCode, Message: strings.TrimSpace(string(b))}
	}

	var parsed struct {
		Items []struct {
			Hit            bool   `json:"hit"`
			FingerprintHex string `json:"fingerprint_hex"`
			ValueB64       string `json:"value_b64,omitempty"`
			Expired        bool   `json:"expired,omitempty"`
			SizeBytes      int64  `json:"size_bytes,omitempty"`
			StoredAt       int64  `json:"stored_at,omitempty"`
			ExpiresAt      int64  `json:"expires_at,omitempty"`
		} `json:"items"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&parsed); err != nil {
		return nil, &APIError{Status: 0, Message: "decode: " + err.Error()}
	}

	results := make([]BatchLookupItem, 0, len(parsed.Items))
	for _, r := range parsed.Items {
		item := BatchLookupItem{
			Hit:            r.Hit,
			FingerprintHex: r.FingerprintHex,
			Expired:        r.Expired,
			SizeBytes:      r.SizeBytes,
			StoredAt:       r.StoredAt,
			ExpiresAt:      r.ExpiresAt,
		}
		if r.Hit && r.ValueB64 != "" {
			decoded, err := base64Decode(r.ValueB64)
			if err != nil {
				return nil, &APIError{Status: 0, Message: "decode value_b64: " + err.Error()}
			}
			item.Value = decoded
		}
		results = append(results, item)
	}
	return results, nil
}

// Session is a chain-aware wrapper around a Client. Every call advances the
// chain by passing the prior record as Prev on the next fingerprint. Use
// this in agent loops where each step's record should link to the prior
// step's.
//
//	session := hypercache.NewSession(client)
//	r1, _ := session.Fingerprint(ctx, []byte("step one"))
//	r2, _ := session.Fingerprint(ctx, []byte("step two")) // chained to r1
//	r3, _ := session.Fingerprint(ctx, []byte("step three")) // chained to r2
type Session struct {
	client *Client
	prev   []byte // nil if no calls yet
	run    string // optional run identifier auto-attached to PUTs and list queries
}

// NewSession returns a new chain-aware session wrapping the given client.
func NewSession(client *Client) *Session {
	return &Session{client: client}
}

// Prev returns the most recent record produced in this session, or nil if
// no calls have been made yet.
func (s *Session) Prev() []byte {
	if s.prev == nil {
		return nil
	}
	out := make([]byte, len(s.prev))
	copy(out, s.prev)
	return out
}

// Reset clears the chain. Subsequent calls start a fresh chain.
func (s *Session) Reset() {
	s.prev = nil
}

// Fingerprint computes a record for data, auto-chained to the previous record
// in this session. The new record is stored as the session's prev for the
// next call.
func (s *Session) Fingerprint(ctx context.Context, data []byte, opts ...FingerprintOption) (*Result, error) {
	allOpts := opts
	if s.prev != nil {
		// Prepend so user-supplied WithPrev (if any) overrides. Last option wins.
		allOpts = append([]FingerprintOption{WithPrev(s.prev)}, opts...)
	}
	r, err := s.client.Fingerprint(ctx, data, allOpts...)
	if err != nil {
		return nil, err
	}
	s.prev = append([]byte(nil), r.Record...)
	return r, nil
}

// Helpers for base64 without bringing encoding/base64 into the import block twice.
func base64Encode(b []byte) string {
	return stdBase64.EncodeToString(b)
}
func base64Decode(s string) ([]byte, error) {
	return stdBase64.DecodeString(s)
}

// ----- Pipeline: all-in template (cache + chain + report) -----

// PipelineStep is one entry in a pipeline's chain.
type PipelineStep struct {
	Label          string
	FingerprintHex string
	CacheHit       *bool // nil = pure record (no compute), true = hit, false = miss
	ElapsedMs      int64
	Bytes          int
}

// PipelineReport is the end-of-pipeline summary.
type PipelineReport struct {
	Name      string
	Steps     []PipelineStep
	StartedAt time.Time
	EndedAt   time.Time
}

func (r *PipelineReport) NSteps() int   { return len(r.Steps) }
func (r *PipelineReport) NHits() int {
	n := 0
	for _, s := range r.Steps {
		if s.CacheHit != nil && *s.CacheHit {
			n++
		}
	}
	return n
}
func (r *PipelineReport) NMisses() int {
	n := 0
	for _, s := range r.Steps {
		if s.CacheHit != nil && !*s.CacheHit {
			n++
		}
	}
	return n
}
func (r *PipelineReport) TotalSeconds() float64 {
	if r.EndedAt.Before(r.StartedAt) {
		return 0
	}
	return r.EndedAt.Sub(r.StartedAt).Seconds()
}

// Chain returns the audit chain: ordered (label, fingerprint_hex) pairs.
func (r *PipelineReport) Chain() [][2]string {
	out := make([][2]string, 0, len(r.Steps))
	for _, s := range r.Steps {
		out = append(out, [2]string{s.Label, s.FingerprintHex})
	}
	return out
}

// Pipeline is the all-in workflow template: cache + chain + report in one object.
//
//	p := hypercache.NewPipeline(client, "translate_user_message")
//	defer p.End()
//	p.Record(ctx, "input", []byte(userMsg))
//	translation, hit, _ := p.Cached(ctx, "gpt_translate", []byte("Translate: "+userMsg), func() (string, error) {
//	    return callOpenAI(...)
//	})
//	p.Record(ctx, "output", []byte(translation))
//	report := p.Report()
type Pipeline struct {
	name    string
	client  *Client
	session *Session
	steps   []PipelineStep
	started time.Time
	ended   time.Time
}

// NewPipeline returns a new all-in workflow pipeline.
func NewPipeline(client *Client, name string) *Pipeline {
	return &Pipeline{
		name:    name,
		client:  client,
		session: NewSession(client),
		started: time.Now(),
	}
}

// Record adds a step to the chain (no caching, used for inputs/outputs).
func (p *Pipeline) Record(ctx context.Context, label string, data []byte) (*PipelineStep, error) {
	t0 := time.Now()
	r, err := p.session.Fingerprint(ctx, data)
	if err != nil {
		return nil, err
	}
	step := PipelineStep{
		Label:          label,
		FingerprintHex: r.RecordHex,
		CacheHit:       nil,
		ElapsedMs:      time.Since(t0).Milliseconds(),
		Bytes:          len(data),
	}
	p.steps = append(p.steps, step)
	return &p.steps[len(p.steps)-1], nil
}

// Cached caches an expensive computation by its input bytes.
// computeFn is called only on cache miss; its return value is stored.
func (p *Pipeline) Cached(
	ctx context.Context,
	label string,
	inputBytes []byte,
	computeFn func() (string, error),
	ttl ...int,
) (string, bool, error) {
	t0 := time.Now()
	storeTTL := 86400
	if len(ttl) > 0 {
		storeTTL = ttl[0]
	}

	// One-call lookup: fingerprint + cache check.
	body, _ := json.Marshal(struct {
		Items []map[string]interface{} `json:"items"`
	}{
		Items: []map[string]interface{}{
			{"data_b64": base64Encode(inputBytes)},
		},
	})
	req, err := http.NewRequestWithContext(ctx, http.MethodPost,
		p.client.baseURL+"/v1/cache/lookup/batch", bytes.NewReader(body))
	if err != nil {
		return "", false, &APIError{Status: 0, Message: "build request: " + err.Error()}
	}
	req.Header.Set("Authorization", "Bearer "+p.client.apiKey)
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", "hypercache-go/"+Version)
	resp, err := p.client.httpClient.Do(req)
	if err != nil {
		return "", false, &APIError{Status: 0, Message: "request failed: " + err.Error()}
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		b, _ := io.ReadAll(resp.Body)
		return "", false, &APIError{Status: resp.StatusCode, Message: strings.TrimSpace(string(b))}
	}
	var lookupResp struct {
		Items []struct {
			Hit            bool   `json:"hit"`
			FingerprintHex string `json:"fingerprint_hex"`
			ValueB64       string `json:"value_b64,omitempty"`
		} `json:"items"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&lookupResp); err != nil {
		return "", false, &APIError{Status: 0, Message: "decode: " + err.Error()}
	}
	if len(lookupResp.Items) == 0 {
		return "", false, &APIError{Status: 0, Message: "empty lookup response"}
	}
	item := lookupResp.Items[0]

	if item.Hit && item.ValueB64 != "" {
		value, err := base64Decode(item.ValueB64)
		if err != nil {
			return "", false, &APIError{Status: 0, Message: "decode value_b64: " + err.Error()}
		}
		hit := true
		p.steps = append(p.steps, PipelineStep{
			Label:          label,
			FingerprintHex: item.FingerprintHex,
			CacheHit:       &hit,
			ElapsedMs:      time.Since(t0).Milliseconds(),
			Bytes:          len(inputBytes),
		})
		return string(value), true, nil
	}

	// Miss — compute and store.
	result, err := computeFn()
	if err != nil {
		return "", false, err
	}
	if _, err := p.client.CachePut(ctx, item.FingerprintHex, []byte(result), WithTTL(storeTTL)); err != nil {
		return "", false, err
	}
	miss := false
	p.steps = append(p.steps, PipelineStep{
		Label:          label,
		FingerprintHex: item.FingerprintHex,
		CacheHit:       &miss,
		ElapsedMs:      time.Since(t0).Milliseconds(),
		Bytes:          len(inputBytes),
	})
	return result, false, nil
}

// End finalizes the pipeline and returns its report.
func (p *Pipeline) End() *PipelineReport {
	p.ended = time.Now()
	return p.Report()
}

// Report returns the current state of the pipeline.
func (p *Pipeline) Report() *PipelineReport {
	ended := p.ended
	if ended.IsZero() {
		ended = time.Now()
	}
	return &PipelineReport{
		Name:      p.name,
		Steps:     append([]PipelineStep(nil), p.steps...),
		StartedAt: p.started,
		EndedAt:   ended,
	}
}

// Module-level convenience: a lazily-initialized default Client.

var defaultClient *Client

// Fingerprint is a module-level shortcut that uses a default Client constructed
// from HYPERCACHE_KEY on first call. Suitable for short scripts; long-running
// services should construct an explicit Client to control timeouts and HTTP transport.
func Fingerprint(ctx context.Context, data []byte, opts ...FingerprintOption) (*Result, error) {
	if defaultClient == nil {
		c, err := NewClient()
		if err != nil {
			return nil, err
		}
		defaultClient = c
	}
	return defaultClient.Fingerprint(ctx, data, opts...)
}

// CachePut is a module-level shortcut. See Client.CachePut.
func CachePut(ctx context.Context, fingerprint string, data []byte, opts ...CachePutOption) (*CachePutResult, error) {
	if defaultClient == nil {
		c, err := NewClient()
		if err != nil {
			return nil, err
		}
		defaultClient = c
	}
	return defaultClient.CachePut(ctx, fingerprint, data, opts...)
}

// CacheGet is a module-level shortcut. See Client.CacheGet.
func CacheGet(ctx context.Context, fingerprint string) ([]byte, error) {
	if defaultClient == nil {
		c, err := NewClient()
		if err != nil {
			return nil, err
		}
		defaultClient = c
	}
	return defaultClient.CacheGet(ctx, fingerprint)
}

// CacheDelete is a module-level shortcut. See Client.CacheDelete.
func CacheDelete(ctx context.Context, fingerprint string) error {
	if defaultClient == nil {
		c, err := NewClient()
		if err != nil {
			return err
		}
		defaultClient = c
	}
	return defaultClient.CacheDelete(ctx, fingerprint)
}

// =============================================================================
// Organizational endpoints: list, relabel, bulk delete
// =============================================================================

// CacheListEntry is one cache entry returned by CacheList.
type CacheListEntry struct {
	FingerprintHex string `json:"fingerprint_hex"`
	Label          string `json:"label,omitempty"`
	Run            string `json:"run,omitempty"`
	SizeBytes      int64  `json:"size_bytes"`
	StoredAt       int64  `json:"stored_at"`
	ExpiresAt      *int64 `json:"expires_at"`
}

// CacheListRunGroup is one run-bucket within a CacheListResponse.
type CacheListRunGroup struct {
	Run        string           `json:"run"`
	Count      int              `json:"count"`
	TotalBytes int64            `json:"total_bytes"`
	Entries    []CacheListEntry `json:"entries"`
}

// CacheListResponse is the result of CacheList. Entries are grouped by run
// inside the chosen bucket window.
type CacheListResponse struct {
	Bucket     string              `json:"bucket"`
	Part       string              `json:"part"`
	TotalCount int                 `json:"total_count"`
	TotalBytes int64               `json:"total_bytes"`
	Runs       []CacheListRunGroup `json:"runs"`
	NextCursor *int                `json:"next_cursor"`
}

// CacheListOption configures a CacheList call.
type CacheListOption func(*cacheListOpts)

type cacheListOpts struct {
	bucket      string
	part        string
	run         string
	labelPrefix string
	limit       int
	cursor      *int
}

// WithBucket sets the time window. One of: "today", "yesterday", "this-week",
// "this-month", "this-year", "YYYY", "YYYY-MM", or "YYYY-MM-DD".
func WithBucket(bucket string) CacheListOption {
	return func(o *cacheListOpts) { o.bucket = bucket }
}

// WithPart filters by time-of-day: "AM", "PM", or "ALL" (default).
func WithPart(part string) CacheListOption {
	return func(o *cacheListOpts) { o.part = part }
}

// WithRunFilter narrows the list to entries with the given run identifier.
func WithRunFilter(run string) CacheListOption {
	return func(o *cacheListOpts) { o.run = run }
}

// WithLabelPrefix narrows the list to entries whose label starts with this prefix.
func WithLabelPrefix(prefix string) CacheListOption {
	return func(o *cacheListOpts) { o.labelPrefix = prefix }
}

// WithLimit sets the max entries per response (default 100, max 500).
func WithLimit(limit int) CacheListOption {
	return func(o *cacheListOpts) { o.limit = limit }
}

// WithCursor sets the pagination cursor (from a previous CacheList.NextCursor).
func WithCursor(cursor int) CacheListOption {
	return func(o *cacheListOpts) { o.cursor = &cursor }
}

// CacheList returns cache entries filtered by time bucket + run + label prefix.
// Cost: 0.25 weighted ops per call.
//
// Default: bucket=today, part=ALL, limit=100, no filters.
func (c *Client) CacheList(ctx context.Context, opts ...CacheListOption) (*CacheListResponse, error) {
	o := &cacheListOpts{bucket: "today", part: "ALL", limit: 100}
	for _, opt := range opts {
		opt(o)
	}

	q := make([]string, 0, 6)
	q = append(q, "bucket="+o.bucket, "part="+o.part, "limit="+strconv.Itoa(o.limit))
	if o.run != "" {
		q = append(q, "run="+urlEncode(o.run))
	}
	if o.labelPrefix != "" {
		q = append(q, "label_prefix="+urlEncode(o.labelPrefix))
	}
	if o.cursor != nil {
		q = append(q, "cursor="+strconv.Itoa(*o.cursor))
	}

	url := c.baseURL + "/v1/cache/list?" + strings.Join(q, "&")
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "build request: " + err.Error()}
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("User-Agent", "hypercache-go/"+Version)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "request failed: " + err.Error()}
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, &APIError{Status: resp.StatusCode, Message: "read body: " + err.Error()}
	}
	if resp.StatusCode != http.StatusOK {
		return nil, &APIError{Status: resp.StatusCode, Message: strings.TrimSpace(string(body))}
	}

	var out CacheListResponse
	if err := json.Unmarshal(body, &out); err != nil {
		return nil, &APIError{Status: 0, Message: "parse: " + err.Error()}
	}
	return &out, nil
}

// CacheRelabelResult is the response from CacheRelabel.
type CacheRelabelResult struct {
	Relabeled      bool   `json:"relabeled"`
	FingerprintHex string `json:"fingerprint_hex"`
	Label          string `json:"label,omitempty"`
	Run            string `json:"run,omitempty"`
}

// CacheRelabelOption configures a CacheRelabel call.
type CacheRelabelOption func(*cacheRelabelOpts)

type cacheRelabelOpts struct {
	setLabel    bool
	label       string
	setRun      bool
	run         string
	clearLabel  bool
	clearRun    bool
}

// WithLabelUpdate sets a new label. Pass an empty string with WithLabelCleared to clear.
func WithLabelUpdate(label string) CacheRelabelOption {
	return func(o *cacheRelabelOpts) { o.setLabel = true; o.label = label }
}

// WithRunUpdate sets a new run.
func WithRunUpdate(run string) CacheRelabelOption {
	return func(o *cacheRelabelOpts) { o.setRun = true; o.run = run }
}

// WithLabelCleared explicitly removes the label.
func WithLabelCleared() CacheRelabelOption {
	return func(o *cacheRelabelOpts) { o.setLabel = true; o.clearLabel = true }
}

// WithRunCleared explicitly removes the run.
func WithRunCleared() CacheRelabelOption {
	return func(o *cacheRelabelOpts) { o.setRun = true; o.clearRun = true }
}

// CacheRelabel updates the label and/or run of an existing cache entry without
// touching its payload.
func (c *Client) CacheRelabel(ctx context.Context, fingerprint string, opts ...CacheRelabelOption) (*CacheRelabelResult, error) {
	o := &cacheRelabelOpts{}
	for _, opt := range opts {
		opt(o)
	}
	if !o.setLabel && !o.setRun {
		return nil, &APIError{Status: 0, Message: "CacheRelabel: pass at least one of WithLabelUpdate/WithRunUpdate/WithLabelCleared/WithRunCleared"}
	}

	body := map[string]interface{}{}
	if o.setLabel {
		if o.clearLabel {
			body["label"] = nil
		} else {
			body["label"] = o.label
		}
	}
	if o.setRun {
		if o.clearRun {
			body["run"] = nil
		} else {
			body["run"] = o.run
		}
	}
	jsonBody, err := json.Marshal(body)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "marshal: " + err.Error()}
	}

	url := c.baseURL + "/v1/cache/" + fingerprint + "/relabel"
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(jsonBody))
	if err != nil {
		return nil, &APIError{Status: 0, Message: "build request: " + err.Error()}
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", "hypercache-go/"+Version)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "request failed: " + err.Error()}
	}
	defer resp.Body.Close()

	respBody, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, &APIError{Status: resp.StatusCode, Message: "read: " + err.Error()}
	}
	if resp.StatusCode != http.StatusOK {
		return nil, &APIError{Status: resp.StatusCode, Message: strings.TrimSpace(string(respBody))}
	}

	var out CacheRelabelResult
	if err := json.Unmarshal(respBody, &out); err != nil {
		return nil, &APIError{Status: 0, Message: "parse: " + err.Error()}
	}
	return &out, nil
}

// BulkDeleteResult is the response from CacheBulkDeleteByLabel and CacheBulkDeleteByAge.
type BulkDeleteResult struct {
	Deleted    int   `json:"deleted"`
	BytesFreed int64 `json:"bytes_freed"`
	CutoffUnix int64 `json:"cutoff_unix,omitempty"` // only set on by-age delete
}

// CacheBulkDeleteByLabel deletes every cache entry whose label starts with the
// given prefix. Two-step safety: first call CacheList with WithLabelPrefix to
// learn the count, then pass that exact integer as confirmCount.
//
// Requires Starter tier or higher.
func (c *Client) CacheBulkDeleteByLabel(ctx context.Context, labelPrefix string, confirmCount int) (*BulkDeleteResult, error) {
	url := c.baseURL + "/v1/cache/by-label?label_prefix=" + urlEncode(labelPrefix) + "&confirm=" + strconv.Itoa(confirmCount)
	return c.doBulkDelete(ctx, url)
}

// CacheBulkDeleteByAge deletes every cache entry older than the given relative
// time (e.g. "30d", "12h", "2w", "1m", "1y"). Two-step safety: first call
// CacheList with the equivalent bucket to learn the count, then pass it as
// confirmCount.
//
// Requires Starter tier or higher.
func (c *Client) CacheBulkDeleteByAge(ctx context.Context, olderThan string, confirmCount int) (*BulkDeleteResult, error) {
	url := c.baseURL + "/v1/cache/by-age?older_than=" + urlEncode(olderThan) + "&confirm=" + strconv.Itoa(confirmCount)
	return c.doBulkDelete(ctx, url)
}

func (c *Client) doBulkDelete(ctx context.Context, url string) (*BulkDeleteResult, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodDelete, url, nil)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "build request: " + err.Error()}
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("User-Agent", "hypercache-go/"+Version)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, &APIError{Status: 0, Message: "request failed: " + err.Error()}
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, &APIError{Status: resp.StatusCode, Message: "read: " + err.Error()}
	}
	if resp.StatusCode != http.StatusOK {
		return nil, &APIError{Status: resp.StatusCode, Message: strings.TrimSpace(string(body))}
	}

	var out BulkDeleteResult
	if err := json.Unmarshal(body, &out); err != nil {
		return nil, &APIError{Status: 0, Message: "parse: " + err.Error()}
	}
	return &out, nil
}

// urlEncode URL-encodes a query value without pulling net/url into the import block.
func urlEncode(s string) string {
	// Simple encoding: percent-encode anything not in the unreserved set.
	const hex = "0123456789ABCDEF"
	var b strings.Builder
	for i := 0; i < len(s); i++ {
		ch := s[i]
		if (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') ||
			ch == '-' || ch == '_' || ch == '.' || ch == '~' || ch == '/' {
			b.WriteByte(ch)
		} else {
			b.WriteByte('%')
			b.WriteByte(hex[ch>>4])
			b.WriteByte(hex[ch&0xF])
		}
	}
	return b.String()
}

// =============================================================================
// Session: auto-attach run to PUTs within a scope
// =============================================================================

// WithRunScope returns a NEW Session that auto-attaches the given run to every
// PUT made through it. The original session is unmodified.
//
//	scoped := session.WithRunScope("agent-abc/turn-5")
//	scoped.CachePut(ctx, fp, payload)  // auto-tagged with run=agent-abc/turn-5
//
// Use this idiomatically — Go doesn't have Python's context manager pattern.
func (s *Session) WithRunScope(run string) *Session {
	return &Session{
		client: s.client,
		prev:   s.prev,
		run:    run,
	}
}

// CachePut stores data, auto-attaching the session's run (if set) as x-hc-run.
// Pass opts to override or set additional CachePut options.
func (s *Session) CachePut(ctx context.Context, fingerprint string, data []byte, opts ...CachePutOption) (*CachePutResult, error) {
	if s.run != "" {
		// Prepend so user-supplied WithRun (if any) overrides.
		opts = append([]CachePutOption{WithRun(s.run)}, opts...)
	}
	return s.client.CachePut(ctx, fingerprint, data, opts...)
}

// CacheList lists via the wrapped client. Falls back to the session's run filter
// if no WithRunFilter option is supplied.
func (s *Session) CacheList(ctx context.Context, opts ...CacheListOption) (*CacheListResponse, error) {
	if s.run != "" {
		opts = append([]CacheListOption{WithRunFilter(s.run)}, opts...)
	}
	return s.client.CacheList(ctx, opts...)
}

// Run returns the run identifier attached to PUTs by this session.
func (s *Session) Run() string { return s.run }
