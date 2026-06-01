/**
 * hypercache — JavaScript client for the HyperCache API.
 *
 * Zero runtime dependencies. Zero build step. Works in Node 18+, Deno, Bun,
 * and modern browsers (any runtime with global fetch).
 *
 * Quickstart:
 *
 *   import { fingerprint } from 'hypercache';
 *   const result = await fingerprint(new Uint8Array(4096));
 *   console.log(result.recordHex);
 *
 * TypeScript types live in ./index.d.ts.
 */

export const VERSION = "0.1.1";

const DEFAULT_BASE_URL = "https://api.hypercache.ai";
const DEFAULT_LAYERS = 32;
const DEFAULT_N_TOK = 64;
const DEFAULT_TIMEOUT_MS = 30_000;

// ---------- Errors ----------

export class HypercacheError extends Error {
  /** @param {string} message @param {number} [status] */
  constructor(message, status) {
    super(message);
    this.name = "HypercacheError";
    /** @type {number | undefined} */
    this.status = status;
  }
}

export class AuthError extends HypercacheError {
  constructor(message, status) { super(message, status); this.name = "AuthError"; }
}
export class QuotaError extends HypercacheError {
  constructor(message, status) { super(message, status); this.name = "QuotaError"; }
}
export class RateLimitError extends HypercacheError {
  constructor(message, status) { super(message, status); this.name = "RateLimitError"; }
}
export class ClientError extends HypercacheError {
  constructor(message, status) { super(message, status); this.name = "ClientError"; }
}
export class ServerError extends HypercacheError {
  constructor(message, status) { super(message, status); this.name = "ServerError"; }
}

// ---------- Client ----------

export class Client {
  /**
   * @param {{ apiKey?: string, baseUrl?: string, timeoutMs?: number }} [opts]
   */
  constructor(opts = {}) {
    const envKey = readEnv("HYPERCACHE_KEY");
    const envUrl = readEnv("HYPERCACHE_BASE_URL");
    /** @type {string} */
    this.apiKey = opts.apiKey ?? envKey ?? "";
    if (!this.apiKey) {
      throw new AuthError(
        "No API key. Pass apiKey or set HYPERCACHE_KEY in your environment."
      );
    }
    /** @type {string} */
    this.baseUrl = (opts.baseUrl ?? envUrl ?? DEFAULT_BASE_URL).replace(/\/$/, "");
    /** @type {number} */
    this.timeoutMs = opts.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  }

  /**
   * @param {Uint8Array | ArrayBuffer | ArrayBufferView | string} data
   * @param {{ layers?: number, nTok?: number, prev?: Uint8Array | string }} [options]
   * @returns {Promise<{ record: Uint8Array, recordHex: string, version: number, opsUsed?: number, opsCap?: number, opsRemaining?: number }>}
   */
  async fingerprint(data, options = {}) {
    const body = toBytes(data);
    const prevHex = coercePrev(options.prev);

    const headers = {
      "authorization": `Bearer ${this.apiKey}`,
      "content-type": "application/octet-stream",
      "x-hc-layers": String(options.layers ?? DEFAULT_LAYERS),
      "x-hc-n-tok": String(options.nTok ?? DEFAULT_N_TOK),
      "user-agent": `hypercache-js/${VERSION}`,
    };
    if (prevHex) headers["x-hc-prev"] = prevHex;

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.timeoutMs);

    let resp;
    try {
      resp = await fetch(`${this.baseUrl}/v1/fingerprint`, {
        method: "POST",
        headers,
        body,
        signal: controller.signal,
      });
    } catch (e) {
      if (e && e.name === "AbortError") {
        throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
      }
      throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
    } finally {
      clearTimeout(timer);
    }

    if (!resp.ok) {
      const errBody = await resp.text().catch(() => "");
      raiseForStatus(resp.status, errBody);
    }

    const json = await resp.json();
    return {
      record: hexToBytes(json.fingerprint_hex),
      recordHex: json.fingerprint_hex,
      version: json.version,
      opsUsed: maybeInt(resp.headers.get("x-hc-ops-used")),
      opsCap: maybeInt(resp.headers.get("x-hc-ops-cap")),
      opsRemaining: maybeInt(resp.headers.get("x-hc-ops-remaining")),
    };
  }
}

// ---------- Cache methods (added to Client prototype) ----------

/**
 * Store data under the given fingerprint.
 * @this {Client}
 * @param {string} fingerprint hex string
 * @param {Uint8Array | ArrayBuffer | ArrayBufferView | string} data
 * @param {{ ttl?: number, label?: string, run?: string }} [options]
 *   ttl: seconds until expiry (tier default if omitted; 0 = no expiry)
 *   label: ≤256-char ASCII organizer (e.g., "prod/song1.v1.3")
 *   run: ≤256-char run/session identifier (e.g., "agent-abc123")
 *
 * Labels and runs are stored as plaintext metadata. Do not include PHI
 * or secrets in them — use opaque identifiers.
 * @returns {Promise<{ sizeBytes: number, expiresAt: number | null, label: string | null, run: string | null, opsUsed?: number, opsCap?: number, opsRemaining?: number }>}
 */
Client.prototype.cachePut = async function (fingerprint, data, options = {}) {
  const body = toBytes(data);
  const headers = {
    "authorization": `Bearer ${this.apiKey}`,
    "content-type": "application/octet-stream",
    "user-agent": `hypercache-js/${VERSION}`,
  };
  if (options.ttl !== undefined && options.ttl !== null) {
    headers["x-hc-ttl"] = String(options.ttl);
  }
  if (options.label !== undefined && options.label !== null) {
    headers["x-hc-label"] = String(options.label);
  }
  if (options.run !== undefined && options.run !== null) {
    headers["x-hc-run"] = String(options.run);
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), this.timeoutMs);
  let resp;
  try {
    resp = await fetch(`${this.baseUrl}/v1/cache/${fingerprint}`, {
      method: "PUT",
      headers,
      body,
      signal: controller.signal,
    });
  } catch (e) {
    if (e && e.name === "AbortError") {
      throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
    }
    throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
  } finally {
    clearTimeout(timer);
  }

  if (!resp.ok) {
    const errBody = await resp.text().catch(() => "");
    raiseForStatus(resp.status, errBody);
  }

  const json = await resp.json();
  return {
    sizeBytes: json.size_bytes,
    expiresAt: json.expires_at,
    label: json.label ?? null,
    run: json.run ?? null,
    opsUsed: maybeInt(resp.headers.get("x-hc-ops-used")),
    opsCap: maybeInt(resp.headers.get("x-hc-ops-cap")),
    opsRemaining: maybeInt(resp.headers.get("x-hc-ops-remaining")),
  };
};

/**
 * Retrieve cached bytes for the given fingerprint.
 * @this {Client}
 * @param {string} fingerprint
 * @returns {Promise<Uint8Array | null>} null on cache miss (404), Uint8Array on hit
 */
Client.prototype.cacheGet = async function (fingerprint) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), this.timeoutMs);
  let resp;
  try {
    resp = await fetch(`${this.baseUrl}/v1/cache/${fingerprint}`, {
      method: "GET",
      headers: {
        "authorization": `Bearer ${this.apiKey}`,
        "user-agent": `hypercache-js/${VERSION}`,
      },
      signal: controller.signal,
    });
  } catch (e) {
    if (e && e.name === "AbortError") {
      throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
    }
    throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
  } finally {
    clearTimeout(timer);
  }

  if (resp.status === 404) return null;
  if (!resp.ok) {
    const errBody = await resp.text().catch(() => "");
    raiseForStatus(resp.status, errBody);
  }
  const buf = await resp.arrayBuffer();
  return new Uint8Array(buf);
};

/**
 * Delete cached entry. Idempotent.
 * @this {Client}
 * @param {string} fingerprint
 * @returns {Promise<void>}
 */
Client.prototype.cacheDelete = async function (fingerprint) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), this.timeoutMs);
  let resp;
  try {
    resp = await fetch(`${this.baseUrl}/v1/cache/${fingerprint}`, {
      method: "DELETE",
      headers: {
        "authorization": `Bearer ${this.apiKey}`,
        "user-agent": `hypercache-js/${VERSION}`,
      },
      signal: controller.signal,
    });
  } catch (e) {
    if (e && e.name === "AbortError") {
      throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
    }
    throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
  } finally {
    clearTimeout(timer);
  }
  if (!resp.ok) {
    const errBody = await resp.text().catch(() => "");
    raiseForStatus(resp.status, errBody);
  }
};

// ---------- Organizational methods: list, relabel, bulk delete ----------

/**
 * List your cache entries filtered by time bucket + run + label prefix.
 * Cost: 0.25 weighted ops per call (D1 query, no R2 reads).
 *
 * @this {Client}
 * @param {{ bucket?: string, part?: "AM"|"PM"|"ALL", run?: string, labelPrefix?: string, limit?: number, cursor?: number }} [options]
 * @returns {Promise<{ bucket: string, part: string, totalCount: number, totalBytes: number, runs: Array<{ run: string|null, count: number, totalBytes: number, entries: Array<{ fingerprintHex: string, label: string|null, run: string|null, sizeBytes: number, storedAt: number, expiresAt: number|null }> }>, nextCursor: number|null }>}
 */
Client.prototype.cacheList = async function (options = {}) {
  const params = new URLSearchParams();
  params.set("bucket", options.bucket ?? "today");
  params.set("part", options.part ?? "ALL");
  params.set("limit", String(options.limit ?? 100));
  if (options.run !== undefined) params.set("run", options.run);
  if (options.labelPrefix !== undefined) params.set("label_prefix", options.labelPrefix);
  if (options.cursor !== undefined) params.set("cursor", String(options.cursor));

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), this.timeoutMs);
  let resp;
  try {
    resp = await fetch(`${this.baseUrl}/v1/cache/list?${params.toString()}`, {
      method: "GET",
      headers: {
        "authorization": `Bearer ${this.apiKey}`,
        "user-agent": `hypercache-js/${VERSION}`,
      },
      signal: controller.signal,
    });
  } catch (e) {
    if (e && e.name === "AbortError") throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
    throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
  } finally { clearTimeout(timer); }

  if (!resp.ok) {
    const errBody = await resp.text().catch(() => "");
    raiseForStatus(resp.status, errBody);
  }
  const json = await resp.json();
  return {
    bucket: json.bucket,
    part: json.part,
    totalCount: json.total_count ?? 0,
    totalBytes: json.total_bytes ?? 0,
    runs: (json.runs || []).map(r => ({
      run: r.run,
      count: r.count ?? 0,
      totalBytes: r.total_bytes ?? 0,
      entries: (r.entries || []).map(e => ({
        fingerprintHex: e.fingerprint_hex,
        label: e.label,
        run: e.run,
        sizeBytes: e.size_bytes ?? 0,
        storedAt: e.stored_at ?? 0,
        expiresAt: e.expires_at,
      })),
    })),
    nextCursor: json.next_cursor ?? null,
  };
};

/**
 * Update label and/or run on an existing cache entry. Pass empty string to clear.
 * @this {Client}
 * @param {string} fingerprint hex string
 * @param {{ label?: string|null, run?: string|null }} options
 * @returns {Promise<{ relabeled: boolean, fingerprintHex: string, label: string|null, run: string|null }>}
 */
Client.prototype.cacheRelabel = async function (fingerprint, options) {
  if (!options || (options.label === undefined && options.run === undefined)) {
    throw new ClientError("cacheRelabel: must provide label or run in options");
  }
  const body = {};
  if (options.label !== undefined) body.label = options.label === "" ? null : options.label;
  if (options.run !== undefined) body.run = options.run === "" ? null : options.run;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), this.timeoutMs);
  let resp;
  try {
    resp = await fetch(`${this.baseUrl}/v1/cache/${fingerprint}/relabel`, {
      method: "POST",
      headers: {
        "authorization": `Bearer ${this.apiKey}`,
        "content-type": "application/json",
        "user-agent": `hypercache-js/${VERSION}`,
      },
      body: JSON.stringify(body),
      signal: controller.signal,
    });
  } catch (e) {
    if (e && e.name === "AbortError") throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
    throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
  } finally { clearTimeout(timer); }

  if (!resp.ok) {
    const errBody = await resp.text().catch(() => "");
    raiseForStatus(resp.status, errBody);
  }
  const json = await resp.json();
  return {
    relabeled: !!json.relabeled,
    fingerprintHex: json.fingerprint_hex ?? fingerprint,
    label: json.label ?? null,
    run: json.run ?? null,
  };
};

/**
 * Bulk delete every cache entry whose label starts with the given prefix.
 * Two-step safety: call cacheList(labelPrefix=...) first to learn count,
 * then pass that exact integer as confirmCount.
 * Requires Starter tier or higher.
 *
 * @this {Client}
 * @param {string} labelPrefix
 * @param {number} confirmCount
 * @returns {Promise<{ deleted: number, bytesFreed: number }>}
 */
Client.prototype.cacheBulkDeleteByLabel = async function (labelPrefix, confirmCount) {
  const params = new URLSearchParams();
  params.set("label_prefix", labelPrefix);
  params.set("confirm", String(confirmCount));

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), this.timeoutMs);
  let resp;
  try {
    resp = await fetch(`${this.baseUrl}/v1/cache/by-label?${params.toString()}`, {
      method: "DELETE",
      headers: {
        "authorization": `Bearer ${this.apiKey}`,
        "user-agent": `hypercache-js/${VERSION}`,
      },
      signal: controller.signal,
    });
  } catch (e) {
    if (e && e.name === "AbortError") throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
    throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
  } finally { clearTimeout(timer); }

  if (!resp.ok) {
    const errBody = await resp.text().catch(() => "");
    raiseForStatus(resp.status, errBody);
  }
  const json = await resp.json();
  return { deleted: json.deleted ?? 0, bytesFreed: json.bytes_freed ?? 0 };
};

/**
 * Bulk delete every cache entry older than the given relative time.
 * Two-step safety pattern same as cacheBulkDeleteByLabel.
 * Requires Starter tier or higher.
 *
 * @this {Client}
 * @param {string} olderThan e.g. "30d", "12h", "2w", "1m", "1y"
 * @param {number} confirmCount
 * @returns {Promise<{ deleted: number, bytesFreed: number, cutoffUnix: number|null }>}
 */
Client.prototype.cacheBulkDeleteByAge = async function (olderThan, confirmCount) {
  const params = new URLSearchParams();
  params.set("older_than", olderThan);
  params.set("confirm", String(confirmCount));

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), this.timeoutMs);
  let resp;
  try {
    resp = await fetch(`${this.baseUrl}/v1/cache/by-age?${params.toString()}`, {
      method: "DELETE",
      headers: {
        "authorization": `Bearer ${this.apiKey}`,
        "user-agent": `hypercache-js/${VERSION}`,
      },
      signal: controller.signal,
    });
  } catch (e) {
    if (e && e.name === "AbortError") throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
    throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
  } finally { clearTimeout(timer); }

  if (!resp.ok) {
    const errBody = await resp.text().catch(() => "");
    raiseForStatus(resp.status, errBody);
  }
  const json = await resp.json();
  return {
    deleted: json.deleted ?? 0,
    bytesFreed: json.bytes_freed ?? 0,
    cutoffUnix: json.cutoff_unix ?? null,
  };
};

// ---------- Cache lookup (combined fingerprint + cache check, 1 op) ----------

/**
 * Combined fingerprint + cache check in one round trip.
 * @this {Client}
 * @param {Uint8Array | ArrayBuffer | ArrayBufferView | string} data
 * @param {{ layers?: number, nTok?: number }} [options]
 * @returns {Promise<{ hit: boolean, fingerprintHex: string, value: Uint8Array | null, expired: boolean, opsUsed?: number, opsCap?: number, opsRemaining?: number }>}
 */
Client.prototype.cacheLookup = async function (data, options = {}) {
  const body = toBytes(data);
  const headers = {
    "authorization": `Bearer ${this.apiKey}`,
    "content-type": "application/octet-stream",
    "x-hc-layers": String(options.layers ?? DEFAULT_LAYERS),
    "x-hc-n-tok": String(options.nTok ?? DEFAULT_N_TOK),
    "user-agent": `hypercache-js/${VERSION}`,
  };
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), this.timeoutMs);
  let resp;
  try {
    resp = await fetch(`${this.baseUrl}/v1/cache/lookup`, {
      method: "POST", headers, body, signal: controller.signal,
    });
  } catch (e) {
    if (e && e.name === "AbortError") throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
    throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
  } finally { clearTimeout(timer); }
  if (!resp.ok) {
    const errBody = await resp.text().catch(() => "");
    raiseForStatus(resp.status, errBody);
  }
  const hitHeader = resp.headers.get("x-hc-cache-hit");
  const fpHeader = resp.headers.get("x-hc-fingerprint") || "";
  const opsUsed = maybeInt(resp.headers.get("x-hc-ops-used"));
  const opsCap = maybeInt(resp.headers.get("x-hc-ops-cap"));
  const opsRemaining = maybeInt(resp.headers.get("x-hc-ops-remaining"));
  if (hitHeader === "1") {
    const buf = new Uint8Array(await resp.arrayBuffer());
    return { hit: true, fingerprintHex: fpHeader, value: buf, expired: false, opsUsed, opsCap, opsRemaining };
  }
  const json = await resp.json();
  return {
    hit: false,
    fingerprintHex: json.fingerprint_hex || fpHeader,
    value: null,
    expired: Boolean(json.expired),
    opsUsed, opsCap, opsRemaining,
  };
};

/**
 * Look up many records in one round trip. Strict all-or-nothing op accounting.
 * @this {Client}
 * @param {Array<Uint8Array | { data: Uint8Array, prev?: Uint8Array | string, layers?: number, nTok?: number }>} inputs
 * @returns {Promise<Array<{ hit: boolean, fingerprintHex: string, value: Uint8Array | null, expired: boolean }>>}
 */
Client.prototype.cacheLookupBatch = async function (inputs) {
  if (!Array.isArray(inputs) || inputs.length === 0) {
    throw new ClientError("cacheLookupBatch: inputs must be a non-empty array");
  }
  const items = inputs.map((item, i) => {
    let data, prev, layers, nTok;
    if (item instanceof Uint8Array || typeof item === "string") {
      data = toBytes(item);
    } else if (item && typeof item === "object") {
      data = toBytes(item.data);
      prev = item.prev;
      layers = item.layers;
      nTok = item.nTok;
    } else {
      throw new ClientError(`cacheLookupBatch: inputs[${i}] is not bytes or an object`);
    }
    const out = { data_b64: _b64encode(data) };
    if (layers !== undefined) out.layers = layers;
    if (nTok !== undefined) out.n_tok = nTok;
    if (prev) out.prev_hex = coercePrev(prev);
    return out;
  });
  const headers = {
    "authorization": `Bearer ${this.apiKey}`,
    "content-type": "application/json",
    "user-agent": `hypercache-js/${VERSION}`,
  };
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), this.timeoutMs);
  let resp;
  try {
    resp = await fetch(`${this.baseUrl}/v1/cache/lookup/batch`, {
      method: "POST", headers, body: JSON.stringify({ items }), signal: controller.signal,
    });
  } catch (e) {
    if (e && e.name === "AbortError") throw new ServerError(`Request timed out after ${this.timeoutMs}ms`);
    throw new ServerError(`Network error: ${(e && e.message) || String(e)}`);
  } finally { clearTimeout(timer); }
  if (!resp.ok) {
    const errBody = await resp.text().catch(() => "");
    raiseForStatus(resp.status, errBody);
  }
  const json = await resp.json();
  return (json.items || []).map((r) => ({
    hit: Boolean(r.hit),
    fingerprintHex: r.fingerprint_hex || "",
    value: r.hit && r.value_b64 ? _b64decode(r.value_b64) : null,
    expired: Boolean(r.expired),
  }));
};

function _b64encode(bytes) {
  let s = "";
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
  if (typeof btoa === "function") return btoa(s);
  return Buffer.from(bytes).toString("base64");
}

function _b64decode(b64) {
  if (typeof atob === "function") {
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
  }
  return new Uint8Array(Buffer.from(b64, "base64"));
}

// ---------- Session: chain-aware wrapper ----------

export class Session {
  /** @param {{ client?: Client, apiKey?: string, run?: string }} [options] */
  constructor(options = {}) {
    this.client = options.client || new Client({ apiKey: options.apiKey });
    /** @type {Uint8Array | undefined} */
    this._prev = undefined;
    /** @type {string | undefined} */
    this._run = options.run;
  }

  get prev() { return this._prev; }
  get run() { return this._run; }
  reset() { this._prev = undefined; }

  /**
   * Run a callback with x-hc-run auto-attached to PUTs inside it.
   * @template T
   * @param {string} runName
   * @param {(session: Session) => Promise<T>} fn
   * @returns {Promise<T>}
   *
   * Example:
   *   await session.withRun("agent-abc/turn-5", async (s) => {
   *     await s.cachePut(fp1, bytes1);
   *     await s.cachePut(fp2, bytes2);
   *   });
   *   // run is restored to whatever it was outside the block.
   *
   * Nests cleanly; inner withRun overrides outer for the inner scope.
   */
  async withRun(runName, fn) {
    const oldRun = this._run;
    this._run = runName;
    try {
      return await fn(this);
    } finally {
      this._run = oldRun;
    }
  }

  async fingerprint(data, options = {}) {
    const opts = { ...options };
    if (this._prev && opts.prev === undefined) opts.prev = this._prev;
    const r = await this.client.fingerprint(data, opts);
    this._prev = r.record;
    return r;
  }

  /** Store via the wrapped client, auto-attaching the session's run if no explicit run is given. */
  async cachePut(fingerprint, data, options = {}) {
    const merged = { ...options };
    if (merged.run === undefined && this._run !== undefined) merged.run = this._run;
    return this.client.cachePut(fingerprint, data, merged);
  }

  /** List via the wrapped client. Falls back to the session's run if no run is given. */
  async cacheList(options = {}) {
    const merged = { ...options };
    if (merged.run === undefined && this._run !== undefined) merged.run = this._run;
    return this.client.cacheList(merged);
  }

  /** Forward to Client.cacheRelabel. */
  async cacheRelabel(fingerprint, options) {
    return this.client.cacheRelabel(fingerprint, options);
  }

  /** Forward to Client.cacheBulkDeleteByLabel. */
  async cacheBulkDeleteByLabel(labelPrefix, confirmCount) {
    return this.client.cacheBulkDeleteByLabel(labelPrefix, confirmCount);
  }

  /** Forward to Client.cacheBulkDeleteByAge. */
  async cacheBulkDeleteByAge(olderThan, confirmCount) {
    return this.client.cacheBulkDeleteByAge(olderThan, confirmCount);
  }
}

// ---------- Pipeline: all-in template (cache + chain + report) ----------

export class Pipeline {
  /** @param {string} name @param {{ client?: Client }} [options] */
  constructor(name, options = {}) {
    this.name = name;
    this._client = options.client || new Client();
    this._session = new Session({ client: this._client });
    /** @type {Array<{ label: string, fingerprintHex: string, cacheHit: boolean | null, elapsedMs: number, bytes: number }>} */
    this.steps = [];
    this._startedAt = Date.now();
    this._endedAt = 0;
  }

  /** Add a step to the chain — used for inputs/outputs you want recorded but not cached. */
  async record(label, data) {
    const t0 = Date.now();
    const r = await this._session.fingerprint(data);
    const elapsed = Date.now() - t0;
    const bytes = (data instanceof Uint8Array) ? data.byteLength : 0;
    const step = { label, fingerprintHex: r.recordHex, cacheHit: null, elapsedMs: elapsed, bytes };
    this.steps.push(step);
    return step;
  }

  /** Cache an expensive computation by its input bytes. */
  async cached(label, inputBytes, computeFn, options = {}) {
    const t0 = Date.now();
    const body = toBytes(inputBytes);
    const lookup = await this._client.cacheLookup(body);
    // Advance the chain regardless of hit/miss
    this._session._prev = hexToBytes(lookup.fingerprintHex);
    if (lookup.hit && lookup.value) {
      const elapsed = Date.now() - t0;
      const step = { label, fingerprintHex: lookup.fingerprintHex, cacheHit: true, elapsedMs: elapsed, bytes: body.byteLength };
      this.steps.push(step);
      const decoded = new TextDecoder("utf-8").decode(lookup.value);
      return [decoded, true];
    }
    const result = await computeFn();
    const resultBytes = (typeof result === "string")
      ? new TextEncoder().encode(result)
      : (result instanceof Uint8Array ? result : new TextEncoder().encode(String(result)));
    await this._client.cachePut(lookup.fingerprintHex, resultBytes, { ttl: options.ttl ?? 86400 });
    const elapsed = Date.now() - t0;
    const step = { label, fingerprintHex: lookup.fingerprintHex, cacheHit: false, elapsedMs: elapsed, bytes: body.byteLength };
    this.steps.push(step);
    return [result, false];
  }

  end() {
    this._endedAt = Date.now();
    return this.report;
  }

  get report() {
    const nHits = this.steps.filter(s => s.cacheHit === true).length;
    const nMisses = this.steps.filter(s => s.cacheHit === false).length;
    return {
      name: this.name,
      steps: this.steps.slice(),
      nSteps: this.steps.length,
      nHits,
      nMisses,
      totalSeconds: Math.max(0, (this._endedAt || Date.now()) - this._startedAt) / 1000,
      chain: this.steps.map(s => [s.label, s.fingerprintHex]),
      exportAudit: () => this.steps.map(s => ({
        label: s.label,
        fingerprint_hex: s.fingerprintHex,
        cache_hit: s.cacheHit,
        elapsed_ms: s.elapsedMs,
        bytes: s.bytes,
      })),
    };
  }
}

// ---------- Module-level convenience ----------

/** @type {Client | undefined} */
let _defaultClient;

function _getDefault() {
  if (!_defaultClient) _defaultClient = new Client();
  return _defaultClient;
}

/**
 * Module-level shortcut. Lazily constructs a default Client from HYPERCACHE_KEY.
 * @param {Uint8Array | ArrayBuffer | ArrayBufferView | string} data
 * @param {{ layers?: number, nTok?: number, prev?: Uint8Array | string, apiKey?: string }} [options]
 */
export async function fingerprint(data, options = {}) {
  if (options.apiKey !== undefined) {
    return new Client({ apiKey: options.apiKey }).fingerprint(data, options);
  }
  return _getDefault().fingerprint(data, options);
}

/**
 * @param {string} fingerprint
 * @param {Uint8Array | ArrayBuffer | ArrayBufferView | string} data
 * @param {{ ttl?: number, apiKey?: string }} [options]
 */
export async function cachePut(fingerprint, data, options = {}) {
  if (options.apiKey !== undefined) {
    return new Client({ apiKey: options.apiKey }).cachePut(fingerprint, data, options);
  }
  return _getDefault().cachePut(fingerprint, data, options);
}

/**
 * @param {string} fingerprint
 * @param {{ apiKey?: string }} [options]
 * @returns {Promise<Uint8Array | null>}
 */
export async function cacheGet(fingerprint, options = {}) {
  if (options.apiKey !== undefined) {
    return new Client({ apiKey: options.apiKey }).cacheGet(fingerprint);
  }
  return _getDefault().cacheGet(fingerprint);
}

/**
 * @param {string} fingerprint
 * @param {{ apiKey?: string }} [options]
 * @returns {Promise<void>}
 */
export async function cacheDelete(fingerprint, options = {}) {
  if (options.apiKey !== undefined) {
    return new Client({ apiKey: options.apiKey }).cacheDelete(fingerprint);
  }
  return _getDefault().cacheDelete(fingerprint);
}

// ---------- Helpers ----------

function readEnv(key) {
  const p = (typeof globalThis !== "undefined" && globalThis.process) || undefined;
  return p && p.env ? p.env[key] : undefined;
}

function toBytes(data) {
  if (data instanceof Uint8Array) return data;
  if (data instanceof ArrayBuffer) return new Uint8Array(data);
  if (ArrayBuffer.isView(data)) {
    return new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
  }
  if (typeof data === "string") return new TextEncoder().encode(data);
  throw new TypeError(
    `hypercache: unsupported data type ${typeof data}. ` +
      "Pass Uint8Array, ArrayBuffer, an ArrayBufferView (e.g. Float32Array), or a string."
  );
}

function coercePrev(prev) {
  if (prev === undefined || prev === "") return "";
  if (typeof prev === "string") return prev;
  return bytesToHex(prev);
}

function bytesToHex(b) {
  let s = "";
  for (let i = 0; i < b.length; i++) s += b[i].toString(16).padStart(2, "0");
  return s;
}

function hexToBytes(h) {
  const out = new Uint8Array(h.length / 2);
  for (let i = 0; i < out.length; i++) {
    out[i] = parseInt(h.slice(i * 2, i * 2 + 2), 16);
  }
  return out;
}

function maybeNum(s) {
  // Weighted ops are fractional (e.g. a cache hit is 1.25). parseInt would
  // truncate "1.25" -> 1 (a WRONG value, silently); parseFloat preserves it.
  if (s === null || s === undefined) return undefined;
  const n = parseFloat(s);
  return Number.isNaN(n) ? undefined : n;
}
// Back-compat alias: call sites use maybeInt.
const maybeInt = maybeNum;

function raiseForStatus(status, body) {
  const msg = (body || "").trim() || `HTTP ${status}`;
  if (status === 401) throw new AuthError(msg, status);
  if (status === 402) throw new QuotaError(msg, status);
  if (status === 429) throw new RateLimitError(msg, status);
  if (status >= 400 && status < 500) throw new ClientError(msg, status);
  throw new ServerError(msg, status);
}
