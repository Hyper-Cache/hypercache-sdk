//! # hypercache
//!
//! Rust client for the HyperCache API. Blocking, no async runtime
//! required. Pure-Rust TLS via rustls.
//!
//! ## Quickstart
//!
//! ```no_run
//! use hypercache::Client;
//!
//! let client = Client::new()?;  // reads HYPERCACHE_KEY from env
//! let result = client.fingerprint(b"\x00".repeat(4096).as_slice())?;
//! println!("{}", result.record_hex);
//! println!("ops remaining: {:?}", result.ops_remaining);
//! # Ok::<(), hypercache::Error>(())
//! ```
//!
//! ## Audit chain (linked records)
//!
//! ```no_run
//! use hypercache::{Client, FingerprintOptions};
//!
//! let client = Client::new()?;
//! let r1 = client.fingerprint(&[1u8; 1024])?;
//! let r2 = client.fingerprint_with(
//!     &[2u8; 1024],
//!     &FingerprintOptions { prev: Some(r1.record.clone()), ..Default::default() },
//! )?;
//! # Ok::<(), hypercache::Error>(())
//! ```

use std::env;
use std::fmt;
use std::io::Read;
use std::time::Duration;

/// Crate version.
pub const VERSION: &str = env!("CARGO_PKG_VERSION");

const DEFAULT_BASE_URL: &str =
    "https://api.hypercache.ai";
const DEFAULT_LAYERS: u32 = 32;
const DEFAULT_N_TOK: u32 = 64;
const DEFAULT_TIMEOUT: Duration = Duration::from_secs(30);

// ---------- Error ----------

/// Errors returned by the SDK.
#[derive(Debug)]
pub enum Error {
    /// 401 — missing or invalid API key.
    Auth(String),
    /// 402 — pass expired or operation cap reached.
    Quota(String),
    /// 429 — over the rate limit (1000 req/min).
    RateLimit(String),
    /// Other 4xx response.
    Client { status: u16, body: String },
    /// 5xx response.
    Server { status: u16, body: String },
    /// Network failure or transport error before reaching the server.
    Network(String),
    /// Response body couldn't be parsed.
    Parse(String),
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Error::Auth(m) => write!(f, "hypercache auth error: {}", m),
            Error::Quota(m) => write!(f, "hypercache quota error: {}", m),
            Error::RateLimit(m) => write!(f, "hypercache rate limit: {}", m),
            Error::Client { status, body } => {
                write!(f, "hypercache client error ({}): {}", status, body)
            }
            Error::Server { status, body } => {
                write!(f, "hypercache server error ({}): {}", status, body)
            }
            Error::Network(m) => write!(f, "hypercache network error: {}", m),
            Error::Parse(m) => write!(f, "hypercache parse error: {}", m),
        }
    }
}

impl std::error::Error for Error {}

impl Error {
    /// True for 401 (missing/invalid API key).
    pub fn is_auth(&self) -> bool {
        matches!(self, Error::Auth(_))
    }
    /// True for 402 (pass expired or op cap reached).
    pub fn is_quota(&self) -> bool {
        matches!(self, Error::Quota(_))
    }
    /// True for 429 (rate limit exceeded).
    pub fn is_rate_limit(&self) -> bool {
        matches!(self, Error::RateLimit(_))
    }
    /// True for any 4xx response (including 401, 402, 429).
    pub fn is_client(&self) -> bool {
        matches!(
            self,
            Error::Auth(_) | Error::Quota(_) | Error::RateLimit(_) | Error::Client { .. }
        )
    }
    /// True for 5xx responses or network/transport failures.
    pub fn is_server(&self) -> bool {
        matches!(self, Error::Server { .. } | Error::Network(_))
    }
}

/// Convenience `Result` alias.
pub type Result<T> = std::result::Result<T, Error>;

// ---------- Result struct ----------

/// Parsed response from `/v1/fingerprint`.
#[derive(Debug, Clone)]
pub struct FingerprintResult {
    /// raw record.
    pub record: Vec<u8>,
    /// hex string.
    pub record_hex: String,
    /// Record format version (currently 2).
    pub version: u32,
    /// Operations used in this pass after this call. `None` if header missing.
    /// Weighted ops are fractional (e.g. a cache hit is 1.25), hence f64.
    pub ops_used: Option<f64>,
    /// Operations cap for the user's pass tier. `None` if header missing.
    pub ops_cap: Option<f64>,
    /// Operations remaining (`ops_cap - ops_used`). `None` if header missing.
    pub ops_remaining: Option<f64>,
}

// ---------- Options ----------

/// Per-call options for `fingerprint_with`.
#[derive(Debug, Default, Clone)]
pub struct FingerprintOptions {
    /// Model layer count hint (default 32).
    pub layers: Option<u32>,
    /// Token count hint (default 64).
    pub n_tok: Option<u32>,
    /// Prior record to link to.
    pub prev: Option<Vec<u8>>,
}

// ---------- Client ----------

/// Hyper Cache API client. Reusable across many calls.
#[derive(Clone)]
pub struct Client {
    api_key: String,
    base_url: String,
    agent: ureq::Agent,
}

impl Client {
    /// Build a client reading `HYPERCACHE_KEY` from the environment.
    pub fn new() -> Result<Self> {
        let api_key = env::var("HYPERCACHE_KEY")
            .ok()
            .filter(|s| !s.is_empty())
            .ok_or_else(|| {
                Error::Auth(
                    "no API key: set HYPERCACHE_KEY or use Client::builder().api_key(...)".into(),
                )
            })?;
        Self::with_api_key(api_key)
    }

    /// Build a client with an explicit API key.
    pub fn with_api_key(api_key: impl Into<String>) -> Result<Self> {
        Client::builder().api_key(api_key).build()
    }

    /// Start a builder for non-default configuration.
    pub fn builder() -> ClientBuilder {
        ClientBuilder::default()
    }

    /// Compute a fingerprint with default options.
    pub fn fingerprint(&self, data: &[u8]) -> Result<FingerprintResult> {
        self.fingerprint_with(data, &FingerprintOptions::default())
    }

    /// Compute a fingerprint with per-call options (layers, n_tok, prev).
    pub fn fingerprint_with(
        &self,
        data: &[u8],
        opts: &FingerprintOptions,
    ) -> Result<FingerprintResult> {
        let url = format!("{}/v1/fingerprint", self.base_url);
        let layers = opts.layers.unwrap_or(DEFAULT_LAYERS);
        let n_tok = opts.n_tok.unwrap_or(DEFAULT_N_TOK);

        let mut req = self
            .agent
            .post(&url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("Content-Type", "application/octet-stream")
            .set("X-Hc-Layers", &layers.to_string())
            .set("X-Hc-N-Tok", &n_tok.to_string())
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));

        if let Some(prev) = &opts.prev {
            req = req.set("X-Hc-Prev", &bytes_to_hex(prev));
        }

        let resp = match req.send_bytes(data) {
            Ok(r) => r,
            Err(ureq::Error::Status(status, resp)) => {
                let body = resp.into_string().unwrap_or_default();
                return Err(status_to_error(status, body));
            }
            Err(ureq::Error::Transport(t)) => {
                return Err(Error::Network(t.to_string()));
            }
        };

        let ops_used = resp.header("X-Hc-Ops-Used").and_then(|s| s.parse().ok());
        let ops_cap = resp.header("X-Hc-Ops-Cap").and_then(|s| s.parse().ok());
        let ops_remaining = resp
            .header("X-Hc-Ops-Remaining")
            .and_then(|s| s.parse().ok());

        let body = resp
            .into_string()
            .map_err(|e| Error::Parse(format!("read body: {}", e)))?;

        let parsed: serde_json::Value = serde_json::from_str(&body)
            .map_err(|e| Error::Parse(format!("invalid JSON: {}", e)))?;

        let record_hex = parsed
            .get("fingerprint_hex")
            .and_then(|v| v.as_str())
            .ok_or_else(|| Error::Parse("response missing fingerprint_hex".into()))?
            .to_string();
        let version = parsed
            .get("version")
            .and_then(|v| v.as_u64())
            .ok_or_else(|| Error::Parse("response missing version".into()))?
            as u32;

        let record = hex_to_bytes(&record_hex).map_err(Error::Parse)?;

        Ok(FingerprintResult {
            record,
            record_hex,
            version,
            ops_used,
            ops_cap,
            ops_remaining,
        })
    }
}

// ---------- Builder ----------

/// Builder for [`Client`]. Use `Client::builder()` to start.
#[derive(Default)]
pub struct ClientBuilder {
    api_key: Option<String>,
    base_url: Option<String>,
    timeout: Option<Duration>,
}

impl ClientBuilder {
    pub fn api_key(mut self, key: impl Into<String>) -> Self {
        self.api_key = Some(key.into());
        self
    }
    pub fn base_url(mut self, url: impl Into<String>) -> Self {
        self.base_url = Some(url.into());
        self
    }
    pub fn timeout(mut self, dur: Duration) -> Self {
        self.timeout = Some(dur);
        self
    }
    pub fn build(self) -> Result<Client> {
        let api_key = self
            .api_key
            .or_else(|| env::var("HYPERCACHE_KEY").ok())
            .filter(|s| !s.is_empty())
            .ok_or_else(|| Error::Auth("no API key".into()))?;
        let base_url = self
            .base_url
            .or_else(|| env::var("HYPERCACHE_BASE_URL").ok())
            .unwrap_or_else(|| DEFAULT_BASE_URL.into())
            .trim_end_matches('/')
            .to_string();
        let agent = ureq::AgentBuilder::new()
            .timeout(self.timeout.unwrap_or(DEFAULT_TIMEOUT))
            .build();
        Ok(Client {
            api_key,
            base_url,
            agent,
        })
    }
}

// ---------- Cache methods ----------

/// Returned by [`Client::cache_put`]. Storage receipt + updated quota counters.
#[derive(Debug, Clone)]
pub struct CachePutResult {
    /// Bytes stored.
    pub size_bytes: u64,
    /// Unix epoch seconds at which the entry expires. `None` if stored with no expiry.
    pub expires_at: Option<u64>,
    /// Optional organizer label echoed back from the request.
    pub label: Option<String>,
    /// Optional run/session identifier echoed back from the request.
    pub run: Option<String>,
    pub ops_used: Option<f64>,
    pub ops_cap: Option<f64>,
    pub ops_remaining: Option<f64>,
}

/// Options for `Client::cache_put_with`. All fields optional.
#[derive(Debug, Clone, Default)]
pub struct CachePutOptions {
    /// Seconds until expiry. `None` = tier default. `Some(0)` = no expiry.
    pub ttl: Option<u64>,
    /// Optional ≤256-char ASCII organizer label.
    pub label: Option<String>,
    /// Optional ≤256-char run/session identifier.
    pub run: Option<String>,
}

impl Client {
    /// Store `data` under the given fingerprint.
    ///
    /// `ttl` is seconds until expiry. Pass `None` for the server default (3600s = 1h)
    /// or `Some(0)` for no expiry.
    pub fn cache_put(
        &self,
        fingerprint: &str,
        data: &[u8],
        ttl: Option<u64>,
    ) -> Result<CachePutResult> {
        let url = format!("{}/v1/cache/{}", self.base_url, fingerprint);
        let mut req = self
            .agent
            .put(&url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("Content-Type", "application/octet-stream")
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));

        if let Some(seconds) = ttl {
            req = req.set("X-Hc-TTL", &seconds.to_string());
        }

        let resp = match req.send_bytes(data) {
            Ok(r) => r,
            Err(ureq::Error::Status(status, resp)) => {
                let body = resp.into_string().unwrap_or_default();
                return Err(status_to_error(status, body));
            }
            Err(ureq::Error::Transport(t)) => return Err(Error::Network(t.to_string())),
        };

        let ops_used = resp.header("X-Hc-Ops-Used").and_then(|s| s.parse().ok());
        let ops_cap = resp.header("X-Hc-Ops-Cap").and_then(|s| s.parse().ok());
        let ops_remaining = resp.header("X-Hc-Ops-Remaining").and_then(|s| s.parse().ok());

        let body = resp
            .into_string()
            .map_err(|e| Error::Parse(format!("read body: {}", e)))?;
        let parsed: serde_json::Value = serde_json::from_str(&body)
            .map_err(|e| Error::Parse(format!("invalid JSON: {}", e)))?;

        let size_bytes = parsed
            .get("size_bytes")
            .and_then(|v| v.as_u64())
            .ok_or_else(|| Error::Parse("missing size_bytes".into()))?;
        let expires_at = parsed.get("expires_at").and_then(|v| v.as_u64());

        let label = parsed
            .get("label")
            .and_then(|v| v.as_str())
            .map(|s| s.to_string());
        let run = parsed
            .get("run")
            .and_then(|v| v.as_str())
            .map(|s| s.to_string());

        Ok(CachePutResult {
            size_bytes,
            expires_at,
            label,
            run,
            ops_used,
            ops_cap,
            ops_remaining,
        })
    }

    /// Like `cache_put`, but accepts an options struct with `ttl`, `label`, and `run`.
    /// Labels and runs are stored as plaintext metadata — do not put PHI or secrets in them.
    pub fn cache_put_with(
        &self,
        fingerprint: &str,
        data: &[u8],
        opts: CachePutOptions,
    ) -> Result<CachePutResult> {
        let url = format!("{}/v1/cache/{}", self.base_url, fingerprint);
        let mut req = self
            .agent
            .put(&url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("Content-Type", "application/octet-stream")
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));

        if let Some(seconds) = opts.ttl {
            req = req.set("X-Hc-TTL", &seconds.to_string());
        }
        if let Some(label) = opts.label.as_ref() {
            req = req.set("X-Hc-Label", label);
        }
        if let Some(run) = opts.run.as_ref() {
            req = req.set("X-Hc-Run", run);
        }

        let resp = match req.send_bytes(data) {
            Ok(r) => r,
            Err(ureq::Error::Status(status, resp)) => {
                let body = resp.into_string().unwrap_or_default();
                return Err(status_to_error(status, body));
            }
            Err(ureq::Error::Transport(t)) => return Err(Error::Network(t.to_string())),
        };

        let ops_used = resp.header("X-Hc-Ops-Used").and_then(|s| s.parse().ok());
        let ops_cap = resp.header("X-Hc-Ops-Cap").and_then(|s| s.parse().ok());
        let ops_remaining = resp.header("X-Hc-Ops-Remaining").and_then(|s| s.parse().ok());

        let body = resp
            .into_string()
            .map_err(|e| Error::Parse(format!("read body: {}", e)))?;
        let parsed: serde_json::Value = serde_json::from_str(&body)
            .map_err(|e| Error::Parse(format!("invalid JSON: {}", e)))?;

        let size_bytes = parsed
            .get("size_bytes")
            .and_then(|v| v.as_u64())
            .ok_or_else(|| Error::Parse("missing size_bytes".into()))?;
        let expires_at = parsed.get("expires_at").and_then(|v| v.as_u64());
        let label = parsed.get("label").and_then(|v| v.as_str()).map(|s| s.to_string());
        let run = parsed.get("run").and_then(|v| v.as_str()).map(|s| s.to_string());

        Ok(CachePutResult {
            size_bytes,
            expires_at,
            label,
            run,
            ops_used,
            ops_cap,
            ops_remaining,
        })
    }

    /// Retrieve cached bytes for the given fingerprint.
    ///
    /// Returns `Ok(Some(bytes))` on cache hit, `Ok(None)` on cache miss (404 is
    /// the expected miss case, not an error). Other HTTP failures return `Err`.
    pub fn cache_get(&self, fingerprint: &str) -> Result<Option<Vec<u8>>> {
        let url = format!("{}/v1/cache/{}", self.base_url, fingerprint);
        let req = self
            .agent
            .get(&url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));

        match req.call() {
            Ok(resp) => {
                let mut buf = Vec::new();
                resp.into_reader()
                    .read_to_end(&mut buf)
                    .map_err(|e| Error::Parse(format!("read body: {}", e)))?;
                Ok(Some(buf))
            }
            Err(ureq::Error::Status(404, _)) => Ok(None),
            Err(ureq::Error::Status(status, resp)) => {
                let body = resp.into_string().unwrap_or_default();
                Err(status_to_error(status, body))
            }
            Err(ureq::Error::Transport(t)) => Err(Error::Network(t.to_string())),
        }
    }

    /// Delete the cached entry. Idempotent — does not error on already-deleted.
    pub fn cache_delete(&self, fingerprint: &str) -> Result<()> {
        let url = format!("{}/v1/cache/{}", self.base_url, fingerprint);
        let req = self
            .agent
            .delete(&url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));

        match req.call() {
            Ok(_) => Ok(()),
            Err(ureq::Error::Status(status, resp)) => {
                let body = resp.into_string().unwrap_or_default();
                Err(status_to_error(status, body))
            }
            Err(ureq::Error::Transport(t)) => Err(Error::Network(t.to_string())),
        }
    }
}

// ---------- Cache lookup (combined fingerprint + cache check, 1 op) ----------

#[derive(Debug, Clone)]
pub struct CacheLookupResult {
    pub hit: bool,
    pub fingerprint_hex: String,
    pub value: Option<Vec<u8>>,
    pub expired: bool,
    pub ops_used: Option<f64>,
    pub ops_cap: Option<f64>,
    pub ops_remaining: Option<f64>,
}

#[derive(Debug, Clone)]
pub struct BatchLookupItem {
    pub hit: bool,
    pub fingerprint_hex: String,
    pub value: Option<Vec<u8>>,
    pub expired: bool,
}

impl Client {
    /// Combined fingerprint + cache check in one round trip. Charges 1 op.
    pub fn cache_lookup(&self, data: &[u8]) -> Result<CacheLookupResult> {
        self.cache_lookup_with(data, &FingerprintOptions::default())
    }

    /// Same as `cache_lookup` with explicit options.
    pub fn cache_lookup_with(
        &self,
        data: &[u8],
        opts: &FingerprintOptions,
    ) -> Result<CacheLookupResult> {
        let url = format!("{}/v1/cache/lookup", self.base_url);
        let mut req = self
            .agent
            .post(&url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("Content-Type", "application/octet-stream")
            .set("X-Hc-Layers", &opts.layers.unwrap_or(DEFAULT_LAYERS).to_string())
            .set("X-Hc-N-Tok", &opts.n_tok.unwrap_or(DEFAULT_N_TOK).to_string())
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));
        if let Some(prev) = &opts.prev {
            req = req.set("X-Hc-Prev", &hex_encode(prev));
        }

        let resp = match req.send_bytes(data) {
            Ok(r) => r,
            Err(ureq::Error::Status(status, resp)) => {
                let body = resp.into_string().unwrap_or_default();
                return Err(status_to_error(status, body));
            }
            Err(ureq::Error::Transport(t)) => return Err(Error::Network(t.to_string())),
        };

        let hit = resp.header("X-Hc-Cache-Hit") == Some("1");
        let fp_header = resp.header("X-Hc-Fingerprint").unwrap_or("").to_string();
        let ops_used = resp.header("X-Hc-Ops-Used").and_then(|s| s.parse().ok());
        let ops_cap = resp.header("X-Hc-Ops-Cap").and_then(|s| s.parse().ok());
        let ops_remaining = resp.header("X-Hc-Ops-Remaining").and_then(|s| s.parse().ok());

        if hit {
            let mut buf = Vec::new();
            resp.into_reader()
                .read_to_end(&mut buf)
                .map_err(|e| Error::Parse(format!("read body: {}", e)))?;
            return Ok(CacheLookupResult {
                hit: true,
                fingerprint_hex: fp_header,
                value: Some(buf),
                expired: false,
                ops_used,
                ops_cap,
                ops_remaining,
            });
        }

        let body = resp
            .into_string()
            .map_err(|e| Error::Parse(format!("read body: {}", e)))?;
        let parsed: serde_json::Value = serde_json::from_str(&body)
            .map_err(|e| Error::Parse(format!("invalid JSON: {}", e)))?;
        Ok(CacheLookupResult {
            hit: false,
            fingerprint_hex: parsed
                .get("fingerprint_hex")
                .and_then(|v| v.as_str())
                .map(String::from)
                .unwrap_or(fp_header),
            value: None,
            expired: parsed
                .get("expired")
                .and_then(|v| v.as_bool())
                .unwrap_or(false),
            ops_used,
            ops_cap,
            ops_remaining,
        })
    }

    /// Look up many records in a single round trip. Strict all-or-nothing op accounting.
    pub fn cache_lookup_batch(&self, inputs: &[&[u8]]) -> Result<Vec<BatchLookupItem>> {
        if inputs.is_empty() {
            return Err(Error::Parse("cache_lookup_batch: empty inputs".into()));
        }
        let items: Vec<serde_json::Value> = inputs
            .iter()
            .map(|d| serde_json::json!({ "data_b64": base64_encode(d) }))
            .collect();
        let body = serde_json::json!({ "items": items }).to_string();

        let url = format!("{}/v1/cache/lookup/batch", self.base_url);
        let req = self
            .agent
            .post(&url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("Content-Type", "application/json")
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));

        let resp = match req.send_string(&body) {
            Ok(r) => r,
            Err(ureq::Error::Status(status, resp)) => {
                let b = resp.into_string().unwrap_or_default();
                return Err(status_to_error(status, b));
            }
            Err(ureq::Error::Transport(t)) => return Err(Error::Network(t.to_string())),
        };

        let txt = resp
            .into_string()
            .map_err(|e| Error::Parse(format!("read body: {}", e)))?;
        let parsed: serde_json::Value = serde_json::from_str(&txt)
            .map_err(|e| Error::Parse(format!("invalid JSON: {}", e)))?;

        let items_v = parsed
            .get("items")
            .and_then(|v| v.as_array())
            .ok_or_else(|| Error::Parse("missing items array".into()))?;
        let mut out = Vec::with_capacity(items_v.len());
        for r in items_v {
            let value = r
                .get("value_b64")
                .and_then(|v| v.as_str())
                .map(base64_decode)
                .transpose()
                .map_err(|e| Error::Parse(format!("decode value_b64: {}", e)))?;
            out.push(BatchLookupItem {
                hit: r.get("hit").and_then(|v| v.as_bool()).unwrap_or(false),
                fingerprint_hex: r
                    .get("fingerprint_hex")
                    .and_then(|v| v.as_str())
                    .map(String::from)
                    .unwrap_or_default(),
                value,
                expired: r.get("expired").and_then(|v| v.as_bool()).unwrap_or(false),
            });
        }
        Ok(out)
    }
}

// ---------- Session: chain-aware wrapper ----------

/// Auto-chains records: every call advances `prev` to the most recent record.
pub struct Session {
    client: Client,
    prev: Option<Vec<u8>>,
    run: Option<String>,
}

impl Session {
    pub fn new(client: Client) -> Self {
        Self { client, prev: None, run: None }
    }

    /// Create a session pre-scoped to a run identifier (auto-attached to PUTs).
    pub fn with_run(client: Client, run: impl Into<String>) -> Self {
        Self { client, prev: None, run: Some(run.into()) }
    }

    pub fn prev(&self) -> Option<&[u8]> {
        self.prev.as_deref()
    }

    /// The run identifier auto-attached to PUTs by this session, if any.
    pub fn run(&self) -> Option<&str> {
        self.run.as_deref()
    }

    /// Set or replace the run identifier for subsequent PUTs and list queries.
    /// Pass `None` to clear.
    pub fn set_run(&mut self, run: Option<String>) {
        self.run = run;
    }

    pub fn reset(&mut self) {
        self.prev = None;
    }

    pub fn fingerprint(&mut self, data: &[u8]) -> Result<FingerprintResult> {
        let opts = FingerprintOptions {
            prev: self.prev.clone(),
            ..Default::default()
        };
        let r = self.client.fingerprint_with(data, &opts)?;
        self.prev = Some(r.record.clone());
        Ok(r)
    }

    /// Store via the wrapped client, auto-attaching the session's run if set.
    pub fn cache_put(
        &self,
        fingerprint: &str,
        data: &[u8],
        mut opts: CachePutOptions,
    ) -> Result<CachePutResult> {
        if opts.run.is_none() {
            opts.run = self.run.clone();
        }
        self.client.cache_put_with(fingerprint, data, opts)
    }

    /// List via the wrapped client. Auto-attaches the session's run as a filter
    /// when no explicit run filter is set in opts.
    pub fn cache_list(&self, mut opts: CacheListOptions) -> Result<CacheListResponse> {
        if opts.run.is_none() {
            opts.run = self.run.clone();
        }
        self.client.cache_list(opts)
    }
}

// ---------- Pipeline: all-in template ----------

#[derive(Debug, Clone)]
pub struct PipelineStep {
    pub label: String,
    pub fingerprint_hex: String,
    /// None = pure record entry, Some(true) = cache hit, Some(false) = cache miss
    pub cache_hit: Option<bool>,
    pub elapsed_ms: u64,
    pub bytes: usize,
}

#[derive(Debug)]
pub struct PipelineReport {
    pub name: String,
    pub steps: Vec<PipelineStep>,
    pub total_seconds: f64,
}

impl PipelineReport {
    pub fn n_steps(&self) -> usize {
        self.steps.len()
    }
    pub fn n_hits(&self) -> usize {
        self.steps.iter().filter(|s| s.cache_hit == Some(true)).count()
    }
    pub fn n_misses(&self) -> usize {
        self.steps.iter().filter(|s| s.cache_hit == Some(false)).count()
    }
    /// Audit chain: ordered (label, fingerprint_hex) pairs.
    pub fn chain(&self) -> Vec<(String, String)> {
        self.steps
            .iter()
            .map(|s| (s.label.clone(), s.fingerprint_hex.clone()))
            .collect()
    }
}

/// All-in workflow template: cache + chain + report in one object.
pub struct Pipeline {
    name: String,
    client: Client,
    session: Session,
    steps: Vec<PipelineStep>,
    started: std::time::Instant,
}

impl Pipeline {
    pub fn new(client: Client, name: impl Into<String>) -> Self {
        Self {
            name: name.into(),
            client: client.clone(),
            session: Session::new(client),
            steps: Vec::new(),
            started: std::time::Instant::now(),
        }
    }

    /// Add a step to the chain (pure record, no caching).
    pub fn record(&mut self, label: impl Into<String>, data: &[u8]) -> Result<&PipelineStep> {
        let t0 = std::time::Instant::now();
        let r = self.session.fingerprint(data)?;
        let step = PipelineStep {
            label: label.into(),
            fingerprint_hex: r.record_hex,
            cache_hit: None,
            elapsed_ms: t0.elapsed().as_millis() as u64,
            bytes: data.len(),
        };
        self.steps.push(step);
        Ok(self.steps.last().unwrap())
    }

    /// Cache an expensive computation by its input bytes.
    /// `compute_fn` is called only on miss; its return value is stored.
    pub fn cached<F>(
        &mut self,
        label: impl Into<String>,
        input_bytes: &[u8],
        compute_fn: F,
        ttl: Option<u64>,
    ) -> Result<(String, bool)>
    where
        F: FnOnce() -> Result<String>,
    {
        let t0 = std::time::Instant::now();
        let lookup = self.client.cache_lookup(input_bytes)?;
        // Advance the chain by adopting the lookup's fingerprint
        self.session.prev = Some(hex_decode(&lookup.fingerprint_hex)?);

        if lookup.hit {
            let value = lookup
                .value
                .ok_or_else(|| Error::Parse("hit but no value".into()))?;
            let text = String::from_utf8(value)
                .map_err(|e| Error::Parse(format!("non-utf8 cached value: {}", e)))?;
            self.steps.push(PipelineStep {
                label: label.into(),
                fingerprint_hex: lookup.fingerprint_hex,
                cache_hit: Some(true),
                elapsed_ms: t0.elapsed().as_millis() as u64,
                bytes: input_bytes.len(),
            });
            return Ok((text, true));
        }

        // Miss — compute and store.
        let result = compute_fn()?;
        self.client
            .cache_put(&lookup.fingerprint_hex, result.as_bytes(), ttl.or(Some(86400)))?;
        self.steps.push(PipelineStep {
            label: label.into(),
            fingerprint_hex: lookup.fingerprint_hex,
            cache_hit: Some(false),
            elapsed_ms: t0.elapsed().as_millis() as u64,
            bytes: input_bytes.len(),
        });
        Ok((result, false))
    }

    /// Finalize and return the report.
    pub fn end(self) -> PipelineReport {
        let total_seconds = self.started.elapsed().as_secs_f64();
        PipelineReport {
            name: self.name,
            steps: self.steps,
            total_seconds,
        }
    }
}

// ---------- Minimal inline base64 (no extra deps) ----------

fn base64_encode(input: &[u8]) -> String {
    const ALPHA: &[u8; 64] =
        b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity((input.len() + 2) / 3 * 4);
    let mut chunks = input.chunks_exact(3);
    for chunk in &mut chunks {
        let b = ((chunk[0] as u32) << 16) | ((chunk[1] as u32) << 8) | (chunk[2] as u32);
        out.push(ALPHA[((b >> 18) & 63) as usize] as char);
        out.push(ALPHA[((b >> 12) & 63) as usize] as char);
        out.push(ALPHA[((b >> 6) & 63) as usize] as char);
        out.push(ALPHA[(b & 63) as usize] as char);
    }
    let rem = chunks.remainder();
    match rem.len() {
        1 => {
            let b = (rem[0] as u32) << 16;
            out.push(ALPHA[((b >> 18) & 63) as usize] as char);
            out.push(ALPHA[((b >> 12) & 63) as usize] as char);
            out.push('=');
            out.push('=');
        }
        2 => {
            let b = ((rem[0] as u32) << 16) | ((rem[1] as u32) << 8);
            out.push(ALPHA[((b >> 18) & 63) as usize] as char);
            out.push(ALPHA[((b >> 12) & 63) as usize] as char);
            out.push(ALPHA[((b >> 6) & 63) as usize] as char);
            out.push('=');
        }
        _ => {}
    }
    out
}

fn base64_decode(s: &str) -> std::result::Result<Vec<u8>, String> {
    let mut buf = Vec::with_capacity(s.len() / 4 * 3);
    let clean: Vec<u8> = s.bytes().filter(|c| !c.is_ascii_whitespace()).collect();
    let val = |c: u8| -> std::result::Result<u32, String> {
        match c {
            b'A'..=b'Z' => Ok((c - b'A') as u32),
            b'a'..=b'z' => Ok((c - b'a' + 26) as u32),
            b'0'..=b'9' => Ok((c - b'0' + 52) as u32),
            b'+' => Ok(62),
            b'/' => Ok(63),
            b'=' => Ok(0),
            _ => Err(format!("invalid base64 char: {}", c as char)),
        }
    };
    let mut i = 0;
    while i + 4 <= clean.len() {
        let a = val(clean[i])?;
        let b = val(clean[i + 1])?;
        let c = val(clean[i + 2])?;
        let d = val(clean[i + 3])?;
        let combined = (a << 18) | (b << 12) | (c << 6) | d;
        buf.push(((combined >> 16) & 0xFF) as u8);
        if clean[i + 2] != b'=' {
            buf.push(((combined >> 8) & 0xFF) as u8);
        }
        if clean[i + 3] != b'=' {
            buf.push((combined & 0xFF) as u8);
        }
        i += 4;
    }
    Ok(buf)
}

fn hex_encode(bytes: &[u8]) -> String {
    let mut s = String::with_capacity(bytes.len() * 2);
    for b in bytes {
        s.push_str(&format!("{:02x}", b));
    }
    s
}

fn hex_decode(s: &str) -> Result<Vec<u8>> {
    if s.len() % 2 != 0 {
        return Err(Error::Parse("odd-length hex".into()));
    }
    let mut out = Vec::with_capacity(s.len() / 2);
    for i in 0..(s.len() / 2) {
        let byte = u8::from_str_radix(&s[i * 2..i * 2 + 2], 16)
            .map_err(|e| Error::Parse(format!("invalid hex: {}", e)))?;
        out.push(byte);
    }
    Ok(out)
}

// ---------- Module-level convenience ----------

/// Shortcut: build a default client from `HYPERCACHE_KEY` and run one fingerprint.
///
/// For high-volume callers, construct a [`Client`] once and reuse it.
pub fn fingerprint(data: &[u8]) -> Result<FingerprintResult> {
    Client::new()?.fingerprint(data)
}

/// Shortcut for [`Client::cache_put`].
pub fn cache_put(fingerprint: &str, data: &[u8], ttl: Option<u64>) -> Result<CachePutResult> {
    Client::new()?.cache_put(fingerprint, data, ttl)
}

/// Shortcut for [`Client::cache_get`].
pub fn cache_get(fingerprint: &str) -> Result<Option<Vec<u8>>> {
    Client::new()?.cache_get(fingerprint)
}

/// Shortcut for [`Client::cache_delete`].
pub fn cache_delete(fingerprint: &str) -> Result<()> {
    Client::new()?.cache_delete(fingerprint)
}

// ---------- Helpers ----------

fn bytes_to_hex(bytes: &[u8]) -> String {
    const HEX: &[u8; 16] = b"0123456789abcdef";
    let mut out = String::with_capacity(bytes.len() * 2);
    for &b in bytes {
        out.push(HEX[(b >> 4) as usize] as char);
        out.push(HEX[(b & 0x0f) as usize] as char);
    }
    out
}

fn hex_to_bytes(hex: &str) -> std::result::Result<Vec<u8>, String> {
    if hex.len() % 2 != 0 {
        return Err("odd-length hex".into());
    }
    let bytes = hex.as_bytes();
    let mut out = Vec::with_capacity(hex.len() / 2);
    for chunk in bytes.chunks(2) {
        let hi = decode_nibble(chunk[0])?;
        let lo = decode_nibble(chunk[1])?;
        out.push((hi << 4) | lo);
    }
    Ok(out)
}

fn decode_nibble(c: u8) -> std::result::Result<u8, String> {
    match c {
        b'0'..=b'9' => Ok(c - b'0'),
        b'a'..=b'f' => Ok(c - b'a' + 10),
        b'A'..=b'F' => Ok(c - b'A' + 10),
        _ => Err(format!("invalid hex character: {:?}", c as char)),
    }
}

fn status_to_error(status: u16, body: String) -> Error {
    let msg = body.trim().to_string();
    match status {
        401 => Error::Auth(msg),
        402 => Error::Quota(msg),
        429 => Error::RateLimit(msg),
        s if (400..500).contains(&s) => Error::Client { status: s, body: msg },
        s => Error::Server { status: s, body: msg },
    }
}

// =============================================================================
// Organizational endpoints: list, relabel, bulk delete
// =============================================================================

/// One cache entry returned by `Client::cache_list`. Metadata only.
#[derive(Debug, Clone)]
pub struct CacheListEntry {
    pub fingerprint_hex: String,
    pub label: Option<String>,
    pub run: Option<String>,
    pub size_bytes: u64,
    pub stored_at: u64,
    pub expires_at: Option<u64>,
}

/// A grouping of cache entries by run within a `CacheListResponse`.
#[derive(Debug, Clone)]
pub struct CacheListRunGroup {
    pub run: Option<String>,
    pub count: u64,
    pub total_bytes: u64,
    pub entries: Vec<CacheListEntry>,
}

/// Response from `Client::cache_list`. Entries grouped by run inside the bucket.
#[derive(Debug, Clone)]
pub struct CacheListResponse {
    pub bucket: String,
    pub part: String,
    pub total_count: u64,
    pub total_bytes: u64,
    pub runs: Vec<CacheListRunGroup>,
    pub next_cursor: Option<u64>,
}

/// Options for `Client::cache_list`. All fields optional with sensible defaults.
#[derive(Debug, Clone, Default)]
pub struct CacheListOptions {
    /// Time bucket: "today" (default), "yesterday", "this-week", "this-month",
    /// "this-year", "YYYY", "YYYY-MM", or "YYYY-MM-DD".
    pub bucket: Option<String>,
    /// Time-of-day filter: "AM", "PM", or "ALL" (default).
    pub part: Option<String>,
    /// Filter by exact run identifier.
    pub run: Option<String>,
    /// Filter by case-sensitive label prefix.
    pub label_prefix: Option<String>,
    /// Max entries per response (default 100, max 500).
    pub limit: Option<u32>,
    /// Pagination cursor from a prior call.
    pub cursor: Option<u64>,
}

/// Response from `Client::cache_relabel`.
#[derive(Debug, Clone)]
pub struct CacheRelabelResult {
    pub relabeled: bool,
    pub fingerprint_hex: String,
    pub label: Option<String>,
    pub run: Option<String>,
}

/// Options for `Client::cache_relabel`. At least one of label/run must be set.
#[derive(Debug, Clone, Default)]
pub struct CacheRelabelOptions {
    /// `Some(s)` sets the label to s. `Some("".into())` clears it. `None` leaves unchanged.
    pub label: Option<String>,
    /// Same semantics as label.
    pub run: Option<String>,
}

/// Response from bulk delete operations.
#[derive(Debug, Clone)]
pub struct BulkDeleteResult {
    pub deleted: u64,
    pub bytes_freed: u64,
    /// Only set on `cache_bulk_delete_by_age`.
    pub cutoff_unix: Option<u64>,
}

impl Client {
    /// List cache entries by time bucket + run + label prefix.
    /// Cost: 0.25 weighted ops per call.
    pub fn cache_list(&self, opts: CacheListOptions) -> Result<CacheListResponse> {
        let bucket = opts.bucket.unwrap_or_else(|| "today".to_string());
        let part = opts.part.unwrap_or_else(|| "ALL".to_string());
        let limit = opts.limit.unwrap_or(100);

        let mut url = format!(
            "{}/v1/cache/list?bucket={}&part={}&limit={}",
            self.base_url, urlenc(&bucket), urlenc(&part), limit
        );
        if let Some(r) = opts.run.as_ref() {
            url.push_str("&run=");
            url.push_str(&urlenc(r));
        }
        if let Some(p) = opts.label_prefix.as_ref() {
            url.push_str("&label_prefix=");
            url.push_str(&urlenc(p));
        }
        if let Some(c) = opts.cursor {
            url.push_str(&format!("&cursor={}", c));
        }

        let req = self.agent.get(&url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));

        let resp = match req.call() {
            Ok(r) => r,
            Err(ureq::Error::Status(s, r)) => return Err(status_to_error(s, r.into_string().unwrap_or_default())),
            Err(ureq::Error::Transport(t)) => return Err(Error::Network(t.to_string())),
        };

        let body = resp.into_string().map_err(|e| Error::Parse(format!("read: {}", e)))?;
        let v: serde_json::Value = serde_json::from_str(&body).map_err(|e| Error::Parse(format!("json: {}", e)))?;

        let runs = v.get("runs").and_then(|x| x.as_array()).map(|arr| {
            arr.iter().map(|r| CacheListRunGroup {
                run: r.get("run").and_then(|x| x.as_str()).map(|s| s.to_string()),
                count: r.get("count").and_then(|x| x.as_u64()).unwrap_or(0),
                total_bytes: r.get("total_bytes").and_then(|x| x.as_u64()).unwrap_or(0),
                entries: r.get("entries").and_then(|x| x.as_array()).map(|arr| {
                    arr.iter().map(|e| CacheListEntry {
                        fingerprint_hex: e.get("fingerprint_hex").and_then(|x| x.as_str()).unwrap_or("").to_string(),
                        label: e.get("label").and_then(|x| x.as_str()).map(|s| s.to_string()),
                        run: e.get("run").and_then(|x| x.as_str()).map(|s| s.to_string()),
                        size_bytes: e.get("size_bytes").and_then(|x| x.as_u64()).unwrap_or(0),
                        stored_at: e.get("stored_at").and_then(|x| x.as_u64()).unwrap_or(0),
                        expires_at: e.get("expires_at").and_then(|x| x.as_u64()),
                    }).collect()
                }).unwrap_or_default(),
            }).collect()
        }).unwrap_or_default();

        Ok(CacheListResponse {
            bucket: v.get("bucket").and_then(|x| x.as_str()).unwrap_or(&bucket).to_string(),
            part: v.get("part").and_then(|x| x.as_str()).unwrap_or(&part).to_string(),
            total_count: v.get("total_count").and_then(|x| x.as_u64()).unwrap_or(0),
            total_bytes: v.get("total_bytes").and_then(|x| x.as_u64()).unwrap_or(0),
            runs,
            next_cursor: v.get("next_cursor").and_then(|x| x.as_u64()),
        })
    }

    /// Update the label and/or run on an existing entry.
    pub fn cache_relabel(&self, fingerprint: &str, opts: CacheRelabelOptions) -> Result<CacheRelabelResult> {
        if opts.label.is_none() && opts.run.is_none() {
            return Err(Error::Client { status: 0, body: "cache_relabel: pass label or run in opts".into() });
        }

        let mut body = serde_json::Map::new();
        if let Some(l) = opts.label.as_ref() {
            body.insert("label".into(), if l.is_empty() { serde_json::Value::Null } else { serde_json::Value::String(l.clone()) });
        }
        if let Some(r) = opts.run.as_ref() {
            body.insert("run".into(), if r.is_empty() { serde_json::Value::Null } else { serde_json::Value::String(r.clone()) });
        }
        let json_body = serde_json::to_vec(&body).map_err(|e| Error::Parse(e.to_string()))?;

        let url = format!("{}/v1/cache/{}/relabel", self.base_url, fingerprint);
        let req = self.agent.post(&url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("Content-Type", "application/json")
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));

        let resp = match req.send_bytes(&json_body) {
            Ok(r) => r,
            Err(ureq::Error::Status(s, r)) => return Err(status_to_error(s, r.into_string().unwrap_or_default())),
            Err(ureq::Error::Transport(t)) => return Err(Error::Network(t.to_string())),
        };

        let body = resp.into_string().map_err(|e| Error::Parse(format!("read: {}", e)))?;
        let v: serde_json::Value = serde_json::from_str(&body).map_err(|e| Error::Parse(format!("json: {}", e)))?;

        Ok(CacheRelabelResult {
            relabeled: v.get("relabeled").and_then(|x| x.as_bool()).unwrap_or(false),
            fingerprint_hex: v.get("fingerprint_hex").and_then(|x| x.as_str()).unwrap_or(fingerprint).to_string(),
            label: v.get("label").and_then(|x| x.as_str()).map(|s| s.to_string()),
            run: v.get("run").and_then(|x| x.as_str()).map(|s| s.to_string()),
        })
    }

    /// Bulk delete entries by label prefix. Two-step safety: pass count from prior cache_list.
    /// Requires Starter tier or higher.
    pub fn cache_bulk_delete_by_label(&self, label_prefix: &str, confirm_count: u64) -> Result<BulkDeleteResult> {
        let url = format!(
            "{}/v1/cache/by-label?label_prefix={}&confirm={}",
            self.base_url, urlenc(label_prefix), confirm_count
        );
        self.do_bulk_delete(&url)
    }

    /// Bulk delete entries older than the given relative time ("30d", "12h", "2w", "1m", "1y").
    /// Two-step safety: pass count from prior cache_list. Requires Starter tier or higher.
    pub fn cache_bulk_delete_by_age(&self, older_than: &str, confirm_count: u64) -> Result<BulkDeleteResult> {
        let url = format!(
            "{}/v1/cache/by-age?older_than={}&confirm={}",
            self.base_url, urlenc(older_than), confirm_count
        );
        self.do_bulk_delete(&url)
    }

    fn do_bulk_delete(&self, url: &str) -> Result<BulkDeleteResult> {
        let req = self.agent.delete(url)
            .set("Authorization", &format!("Bearer {}", self.api_key))
            .set("User-Agent", &format!("hypercache-rust/{}", VERSION));

        let resp = match req.call() {
            Ok(r) => r,
            Err(ureq::Error::Status(s, r)) => return Err(status_to_error(s, r.into_string().unwrap_or_default())),
            Err(ureq::Error::Transport(t)) => return Err(Error::Network(t.to_string())),
        };

        let body = resp.into_string().map_err(|e| Error::Parse(format!("read: {}", e)))?;
        let v: serde_json::Value = serde_json::from_str(&body).map_err(|e| Error::Parse(format!("json: {}", e)))?;

        Ok(BulkDeleteResult {
            deleted: v.get("deleted").and_then(|x| x.as_u64()).unwrap_or(0),
            bytes_freed: v.get("bytes_freed").and_then(|x| x.as_u64()).unwrap_or(0),
            cutoff_unix: v.get("cutoff_unix").and_then(|x| x.as_u64()),
        })
    }
}

/// Minimal URL-encode for query values. Percent-encodes anything outside the unreserved set.
fn urlenc(s: &str) -> String {
    const HEX: &[u8] = b"0123456789ABCDEF";
    let mut out = String::with_capacity(s.len());
    for &b in s.as_bytes() {
        if (b'A'..=b'Z').contains(&b) || (b'a'..=b'z').contains(&b) || (b'0'..=b'9').contains(&b)
            || b == b'-' || b == b'_' || b == b'.' || b == b'~' || b == b'/' {
            out.push(b as char);
        } else {
            out.push('%');
            out.push(HEX[(b >> 4) as usize] as char);
            out.push(HEX[(b & 0xF) as usize] as char);
        }
    }
    out
}

// ---------- Unit tests (no network) ----------

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bytes_hex_roundtrip() {
        let original = vec![0x00, 0x42, 0xff, 0xab, 0xcd];
        let encoded = bytes_to_hex(&original);
        assert_eq!(encoded, "0042ffabcd");
        let decoded = hex_to_bytes(&encoded).unwrap();
        assert_eq!(decoded, original);
    }

    #[test]
    fn error_predicates_match_status() {
        assert!(Error::Auth("x".into()).is_auth());
        assert!(Error::Auth("x".into()).is_client());
        assert!(!Error::Auth("x".into()).is_server());
        assert!(Error::Quota("x".into()).is_quota());
        assert!(Error::RateLimit("x".into()).is_rate_limit());
        assert!(Error::Server { status: 500, body: "x".into() }.is_server());
        assert!(Error::Network("x".into()).is_server());
    }

    #[test]
    fn hex_decode_rejects_invalid() {
        assert!(hex_to_bytes("xx").is_err());
        assert!(hex_to_bytes("abc").is_err()); // odd length
    }
}
