// hypercache — TypeScript declarations.
// Keep this file in sync with index.js when the public API changes.

export const VERSION: string;

// ---------- Errors ----------

export class HypercacheError extends Error {
  status?: number;
  constructor(message: string, status?: number);
}

export class AuthError extends HypercacheError {}
export class QuotaError extends HypercacheError {}
export class RateLimitError extends HypercacheError {}
export class ClientError extends HypercacheError {}
export class ServerError extends HypercacheError {}

// ---------- Types ----------

export type FingerprintInput =
  | Uint8Array
  | ArrayBuffer
  | ArrayBufferView
  | string;

export interface FingerprintOptions {
  /** Model layer count hint (default 32). */
  layers?: number;
  /** Token count hint (default 64). */
  nTok?: number;
  /** Prior record to link to (bytes or hex string). */
  prev?: Uint8Array | string;
}

export interface FingerprintResult {
  /** raw record. */
  record: Uint8Array;
  /** hex string. */
  recordHex: string;
  /** Record format version (currently 2). */
  version: number;
  /** Operations used in this pass after this call. */
  opsUsed?: number;
  /** Operations cap for the user's pass tier. */
  opsCap?: number;
  /** Operations remaining (opsCap - opsUsed). */
  opsRemaining?: number;
}

export interface ClientOptions {
  /** API key. Falls back to process.env.HYPERCACHE_KEY in Node-like runtimes. */
  apiKey?: string;
  /** API base URL. Falls back to process.env.HYPERCACHE_BASE_URL or the production default (https://api.hypercache.ai). */
  baseUrl?: string;
  /** Request timeout in milliseconds (default 30000). */
  timeoutMs?: number;
}

// ---------- Client ----------

export interface CachePutOptions {
  /** Seconds until expiry. Omit for tier default. 0 = no expiry. */
  ttl?: number;
  /** Optional ≤256-char organizer label (e.g., "prod/song1.v1.3"). */
  label?: string;
  /** Optional ≤256-char run/session identifier (e.g., "agent-abc/turn-5"). */
  run?: string;
}

export interface CachePutResult {
  sizeBytes: number;
  expiresAt: number | null;
  label: string | null;
  run: string | null;
  opsUsed?: number;
  opsCap?: number;
  opsRemaining?: number;
}

export type BucketLabel =
  | "today" | "yesterday" | "this-week" | "this-month" | "this-year"
  | string; // also accepts "YYYY", "YYYY-MM", "YYYY-MM-DD"

export interface CacheListOptions {
  /** Time bucket (default "today"). */
  bucket?: BucketLabel;
  /** Time-of-day filter (default "ALL"). */
  part?: "AM" | "PM" | "ALL";
  /** Filter by exact run identifier. */
  run?: string;
  /** Filter by case-sensitive label prefix. */
  labelPrefix?: string;
  /** Max entries per response (default 100, max 500). */
  limit?: number;
  /** Pagination cursor from a previous .nextCursor. */
  cursor?: number;
}

export interface CacheListEntry {
  fingerprintHex: string;
  label: string | null;
  run: string | null;
  sizeBytes: number;
  storedAt: number;          // unix epoch seconds
  expiresAt: number | null;
}

export interface CacheListRunGroup {
  run: string | null;
  count: number;
  totalBytes: number;
  entries: CacheListEntry[];
}

export interface CacheListResponse {
  bucket: string;
  part: "AM" | "PM" | "ALL";
  totalCount: number;
  totalBytes: number;
  runs: CacheListRunGroup[];
  nextCursor: number | null;
}

export interface CacheLookupResult {
  /** True if the input's fingerprint was found in the cache. */
  hit: boolean;
  /** hex fingerprint of the input. */
  fingerprintHex: string;
  /** Cached bytes on a hit; null on a miss. */
  value: Uint8Array | null;
  /** True if the entry existed but had expired (treated as a miss). */
  expired: boolean;
  opsUsed?: number;
  opsCap?: number;
  opsRemaining?: number;
}

export interface CacheLookupBatchItem {
  data: FingerprintInput;
  prev?: Uint8Array | string;
  layers?: number;
  nTok?: number;
}

export interface CacheLookupBatchResult {
  hit: boolean;
  fingerprintHex: string;
  value: Uint8Array | null;
  expired: boolean;
}

export interface CacheRelabelOptions {
  /** New label. Pass null or empty string to clear. */
  label?: string | null;
  /** New run. Pass null or empty string to clear. */
  run?: string | null;
}

export interface CacheRelabelResult {
  relabeled: boolean;
  fingerprintHex: string;
  label: string | null;
  run: string | null;
}

export interface BulkDeleteResult {
  deleted: number;
  bytesFreed: number;
  /** Only set on by-age delete. */
  cutoffUnix?: number | null;
}

export class Client {
  readonly apiKey: string;
  readonly baseUrl: string;
  readonly timeoutMs: number;
  constructor(opts?: ClientOptions);
  fingerprint(data: FingerprintInput, options?: FingerprintOptions): Promise<FingerprintResult>;
  /** Combined fingerprint + cache check in one round trip (1 op). */
  cacheLookup(data: FingerprintInput, options?: FingerprintOptions): Promise<CacheLookupResult>;
  /** Look up many records in one round trip. Strict all-or-nothing op accounting. */
  cacheLookupBatch(inputs: Array<FingerprintInput | CacheLookupBatchItem>): Promise<CacheLookupBatchResult[]>;
  /** Store bytes under the given fingerprint. */
  cachePut(
    fingerprint: string,
    data: FingerprintInput,
    options?: CachePutOptions,
  ): Promise<CachePutResult>;
  /** Retrieve cached bytes. Returns null on cache miss (404). */
  cacheGet(fingerprint: string): Promise<Uint8Array | null>;
  /** Delete cached entry. Idempotent. */
  cacheDelete(fingerprint: string): Promise<void>;
  /** List cache entries by time bucket + run + label prefix. Cost ~0.25 op. */
  cacheList(options?: CacheListOptions): Promise<CacheListResponse>;
  /** Update label and/or run on an existing entry. Pass empty string to clear. */
  cacheRelabel(fingerprint: string, options: CacheRelabelOptions): Promise<CacheRelabelResult>;
  /** Bulk delete by label prefix. Two-step safety: pass count from prior cacheList. */
  cacheBulkDeleteByLabel(labelPrefix: string, confirmCount: number): Promise<BulkDeleteResult>;
  /** Bulk delete by age (e.g. "30d", "12h"). Two-step safety: pass count from prior cacheList. */
  cacheBulkDeleteByAge(olderThan: string, confirmCount: number): Promise<BulkDeleteResult>;
}

// ---------- Session ----------

export interface SessionOptions {
  client?: Client;
  apiKey?: string;
  /** Optional run name auto-attached to PUTs from this session. */
  run?: string;
}

export class Session {
  readonly client: Client;
  readonly prev: Uint8Array | undefined;
  readonly run: string | undefined;
  constructor(options?: SessionOptions);
  reset(): void;
  /** Run callback with auto-attached x-hc-run; restores prior run on exit. */
  withRun<T>(runName: string, fn: (session: Session) => Promise<T>): Promise<T>;
  fingerprint(data: FingerprintInput, options?: FingerprintOptions): Promise<FingerprintResult>;
  cacheLookup(data: FingerprintInput, options?: FingerprintOptions): Promise<CacheLookupResult>;
  cachePut(fingerprint: string, data: FingerprintInput, options?: CachePutOptions): Promise<CachePutResult>;
  cacheList(options?: CacheListOptions): Promise<CacheListResponse>;
  cacheRelabel(fingerprint: string, options: CacheRelabelOptions): Promise<CacheRelabelResult>;
  cacheBulkDeleteByLabel(labelPrefix: string, confirmCount: number): Promise<BulkDeleteResult>;
  cacheBulkDeleteByAge(olderThan: string, confirmCount: number): Promise<BulkDeleteResult>;
}

// ---------- Pipeline ----------

export interface PipelineStep {
  label: string;
  fingerprintHex: string;
  cacheHit: boolean | null;
  elapsedMs: number;
  bytes: number;
}

export interface PipelineReport {
  name: string;
  steps: PipelineStep[];
  nSteps: number;
  nHits: number;
  nMisses: number;
  totalSeconds: number;
  chain: Array<[string, string]>;
  exportAudit: () => Array<{
    label: string;
    fingerprint_hex: string;
    cache_hit: boolean | null;
    elapsed_ms: number;
    bytes: number;
  }>;
}

export class Pipeline {
  readonly name: string;
  readonly steps: PipelineStep[];
  constructor(name: string, options?: { client?: Client });
  /** Record a step (fingerprinted + chained) without caching. */
  record(label: string, data: FingerprintInput): Promise<PipelineStep>;
  /** Cache an expensive computation by its input bytes. Returns [result, wasHit]. */
  cached<T = string>(
    label: string,
    inputBytes: FingerprintInput,
    computeFn: () => T | Promise<T>,
    options?: { ttl?: number },
  ): Promise<[T | string, boolean]>;
  end(): PipelineReport;
  readonly report: PipelineReport;
}

// ---------- Module-level convenience ----------

export function fingerprint(
  data: FingerprintInput,
  options?: FingerprintOptions & { apiKey?: string },
): Promise<FingerprintResult>;

export function cachePut(
  fingerprint: string,
  data: FingerprintInput,
  options?: CachePutOptions & { apiKey?: string },
): Promise<CachePutResult>;

export function cacheGet(
  fingerprint: string,
  options?: { apiKey?: string },
): Promise<Uint8Array | null>;

export function cacheDelete(
  fingerprint: string,
  options?: { apiKey?: string },
): Promise<void>;
