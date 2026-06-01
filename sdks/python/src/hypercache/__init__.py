"""
hypercache — Python client for the HyperCache API.

Quickstart:
    import hypercache

    # Reads HYPERCACHE_KEY from environment by default
    result = hypercache.fingerprint(my_bytes_or_array)
    print(result.record_hex)
    print(result.ops_remaining)      # ops left in your pass

Accepts bytes, bytearray, memoryview, numpy.ndarray, torch.Tensor, or any
buffer-protocol object.

Audit chain (records linked to a prior record):
    r1 = hypercache.fingerprint(batch1)
    r2 = hypercache.fingerprint(batch2, prev=r1.record)
"""

from __future__ import annotations

import json
import os
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Any, Optional, Union

__version__ = "0.1.2"
__all__ = [
    "Client",
    "Session",
    "FingerprintResult",
    "CachePutResult",
    "CacheLookupResult",
    "BatchLookupItem",
    "EmbeddingResult",
    "CacheListEntry",
    "CacheListRun",
    "CacheListResponse",
    "RelabelResult",
    "BulkDeleteResult",
    "HypercacheError",
    "AuthError",
    "QuotaError",
    "RateLimitError",
    "ClientError",
    "ServerError",
    "fingerprint",
    "cache_put",
    "cache_get",
    "cache_delete",
    "cache_lookup",
    "cache_lookup_batch",
    "cached_embedding",
]

DEFAULT_BASE_URL = "https://api.hypercache.ai"
DEFAULT_LAYERS = 32
DEFAULT_N_TOK = 64
DEFAULT_TIMEOUT = 30.0


# ---------- Errors ----------

class HypercacheError(Exception):
    """Base class for all Hyper Cache errors."""

    def __init__(self, message: str, status: Optional[int] = None):
        super().__init__(message)
        self.status = status


class AuthError(HypercacheError):
    """401 — missing or invalid API key."""


class QuotaError(HypercacheError):
    """402 — pass expired or operation cap reached."""


class RateLimitError(HypercacheError):
    """429 — too many requests (1000/min limit)."""


class ClientError(HypercacheError):
    """400-499 (other) — malformed request."""


class ServerError(HypercacheError):
    """5xx or network failure — server-side issue."""


# ---------- Result ----------

@dataclass
class FingerprintResult:
    """Returned by Client.fingerprint()."""

    record: bytes
    record_hex: str
    version: int
    ops_used: Optional[float] = None
    ops_cap: Optional[float] = None
    ops_remaining: Optional[float] = None


@dataclass
class CachePutResult:
    """Returned by Client.cache_put(). Storage receipt + quota metadata."""

    size_bytes: int
    expires_at: Optional[int]  # unix epoch seconds, or None if stored with no expiry
    ops_used: Optional[float] = None
    ops_cap: Optional[float] = None
    ops_remaining: Optional[float] = None


@dataclass
class CacheLookupResult:
    """Returned by Client.cache_lookup(). Combined fingerprint + cache check in 1 op.

    On a hit, ``value`` holds the cached bytes. On a miss, ``value`` is None and
    you should compute the result locally and call ``cache_put(fingerprint_hex, ...)``
    to store it for next time.
    """

    hit: bool
    fingerprint_hex: str
    value: Optional[bytes]
    expired: bool = False  # True if the miss was due to TTL expiration (diagnostic)
    ops_used: Optional[float] = None
    ops_cap: Optional[float] = None
    ops_remaining: Optional[float] = None


@dataclass
class EmbeddingResult:
    """Returned by cached_embedding(). The embedding vector plus diagnostics."""

    embedding: list  # list[float], but kept untyped to avoid numpy entanglement
    hit: bool
    fingerprint_hex: str
    ops_used: Optional[float] = None
    ops_remaining: Optional[float] = None


@dataclass
class BatchLookupItem:
    """One item in a batch lookup result. Mirrors the JSON shape returned by
    POST /v1/cache/lookup/batch.
    """

    hit: bool
    fingerprint_hex: str
    value: Optional[bytes] = None       # decoded from value_b64 on hit
    expired: bool = False               # True if miss was due to TTL expiration
    size_bytes: Optional[int] = None    # cached object size, if hit
    stored_at: Optional[int] = None     # unix epoch seconds, if hit
    expires_at: Optional[int] = None    # unix epoch seconds or None, if hit


@dataclass
class CacheListEntry:
    """One cache entry returned by GET /v1/cache/list.

    Lightweight metadata only — fetch the actual bytes with cache_get(fingerprint_hex).
    """

    fingerprint_hex: str
    label: Optional[str]
    run: Optional[str]
    size_bytes: int
    stored_at: int                       # unix epoch seconds
    expires_at: Optional[int] = None     # unix epoch seconds, or None if no TTL


@dataclass
class CacheListRun:
    """A grouping of cache entries by run name within a bucket window."""

    run: Optional[str]                   # None = entries without a run tag
    count: int
    total_bytes: int
    entries: list                         # list[CacheListEntry]


@dataclass
class CacheListResponse:
    """Response from Client.cache_list().

    Entries are grouped by run inside the bucket window. Use next_cursor to
    paginate; pass it as cursor= on the next call. None = no more pages.
    """

    bucket: str                          # friendly label like "today (2026-05-28)"
    part: str                            # "AM" | "PM" | "ALL"
    total_count: int
    total_bytes: int
    runs: list                            # list[CacheListRun]
    next_cursor: Optional[int] = None


@dataclass
class RelabelResult:
    """Response from Client.cache_relabel()."""

    relabeled: bool
    fingerprint_hex: str
    label: Optional[str] = None
    run: Optional[str] = None


@dataclass
class BulkDeleteResult:
    """Response from cache_bulk_delete_by_label() and cache_bulk_delete_by_age()."""

    deleted: int                         # number of entries removed
    bytes_freed: int                     # total payload bytes reclaimed
    cutoff_unix: Optional[int] = None    # only set on by-age delete


# ---------- Coercion helpers ----------

def _coerce_to_bytes(data: Any) -> bytes:
    """Accept bytes, numpy arrays, torch tensors, buffer-protocol objects, etc."""
    if isinstance(data, (bytes, bytearray)):
        return bytes(data)
    if isinstance(data, memoryview):
        return data.tobytes()
    # torch.Tensor: has detach() and cpu()
    if hasattr(data, "detach") and hasattr(data, "cpu") and hasattr(data, "numpy"):
        return data.detach().cpu().numpy().tobytes()
    # numpy.ndarray and other buffer-protocol objects
    if hasattr(data, "tobytes"):
        return data.tobytes()
    raise TypeError(
        f"hypercache: unsupported data type {type(data).__name__}. "
        "Pass bytes, numpy.ndarray, torch.Tensor, or any buffer-protocol object."
    )


def _coerce_prev(prev: Optional[Union[bytes, bytearray, str]]) -> str:
    if prev is None or prev == "":
        return ""
    if isinstance(prev, (bytes, bytearray)):
        return bytes(prev).hex()
    if isinstance(prev, str):
        return prev
    raise TypeError(
        f"hypercache: prev must be bytes or hex string, got {type(prev).__name__}"
    )


def _raise_for_status(status: int, body: str) -> None:
    message = body.strip() or f"HTTP {status}"
    if status == 401:
        raise AuthError(message, status=status)
    if status == 402:
        raise QuotaError(message, status=status)
    if status == 429:
        raise RateLimitError(message, status=status)
    if 400 <= status < 500:
        raise ClientError(message, status=status)
    raise ServerError(message, status=status)


# ---------- Client ----------

class Client:
    """Hyper Cache API client.

    Args:
        api_key: API key. Falls back to HYPERCACHE_KEY environment variable.
        base_url: API base URL. Falls back to HYPERCACHE_BASE_URL env var or
                  the production default (https://api.hypercache.ai).
        timeout: Request timeout in seconds (default: 30).
    """

    def __init__(
        self,
        api_key: Optional[str] = None,
        base_url: Optional[str] = None,
        timeout: float = DEFAULT_TIMEOUT,
    ):
        self.api_key = api_key or os.environ.get("HYPERCACHE_KEY", "")
        if not self.api_key:
            raise AuthError(
                "No API key. Pass api_key= or set HYPERCACHE_KEY in your environment."
            )
        self.base_url = (
            base_url or os.environ.get("HYPERCACHE_BASE_URL") or DEFAULT_BASE_URL
        ).rstrip("/")
        self.timeout = timeout

    def fingerprint(
        self,
        data: Any,
        layers: int = DEFAULT_LAYERS,
        n_tok: int = DEFAULT_N_TOK,
        prev: Optional[Union[bytes, bytearray, str]] = None,
    ) -> FingerprintResult:
        """Compute a fingerprint for the given data.

        Args:
            data: bytes, numpy.ndarray, torch.Tensor, or any buffer-protocol object.
            layers: model layer count hint (default 32).
            n_tok: token count hint (default 64).
            prev: optional prior record (bytes or hex string) to chain to.

        Returns:
            FingerprintResult with .record, .record_hex, .version,
            and quota metadata (.ops_used, .ops_cap, .ops_remaining).
        """
        body = _coerce_to_bytes(data)
        prev_hex = _coerce_prev(prev)

        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "content-type": "application/octet-stream",
            "x-hc-layers": str(layers),
            "x-hc-n-tok": str(n_tok),
            "user-agent": f"hypercache-python/{__version__}",
        }
        if prev_hex:
            headers["x-hc-prev"] = prev_hex

        req = urllib.request.Request(
            f"{self.base_url}/v1/fingerprint",
            data=body,
            method="POST",
            headers=headers,
        )

        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                payload = json.loads(resp.read())
                return FingerprintResult(
                    record=bytes.fromhex(payload["fingerprint_hex"]),
                    record_hex=payload["fingerprint_hex"],
                    version=payload["version"],
                    ops_used=_maybe_int(resp.headers.get("x-hc-ops-used")),
                    ops_cap=_maybe_int(resp.headers.get("x-hc-ops-cap")),
                    ops_remaining=_maybe_int(resp.headers.get("x-hc-ops-remaining")),
                )
        except urllib.error.HTTPError as e:
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise  # _raise_for_status always raises; this satisfies the type checker
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")

    # ---------- Cache methods ----------

    def cache_put(
        self,
        fingerprint: str,
        data: Any,
        ttl: Optional[int] = None,
        label: Optional[str] = None,
        run: Optional[str] = None,
    ) -> CachePutResult:
        """Store data under the given fingerprint.

        Args:
            fingerprint: hex string from a FingerprintResult.record_hex.
            data: bytes-like or buffer-protocol object to cache.
            ttl: seconds until expiry. None = tier default.
                 Pass 0 for no expiry (object persists until quota pressure or manual delete).
            label: optional ≤256-char ASCII organizer string (e.g., "prod/song1.v1.3").
                   Stored as plaintext metadata — DO NOT put PHI or secrets in labels.
            run: optional ≤256-char run/session identifier (e.g., "agent-abc123").
                 Use for grouping related entries; query via cache_list(run=...).

        Returns:
            CachePutResult with size_bytes, expires_at, and updated quota counters.

        Raises:
            QuotaError on 402 (op cap reached or cache quota exceeded).
            ClientError on 400/413 (bad fingerprint, empty body, object too large).
        """
        body = _coerce_to_bytes(data)
        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "content-type": "application/octet-stream",
            "user-agent": f"hypercache-python/{__version__}",
        }
        if ttl is not None:
            headers["x-hc-ttl"] = str(ttl)
        if label is not None:
            headers["x-hc-label"] = label
        if run is not None:
            headers["x-hc-run"] = run

        req = urllib.request.Request(
            f"{self.base_url}/v1/cache/{fingerprint}",
            data=body,
            method="PUT",
            headers=headers,
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                payload = json.loads(resp.read())
                return CachePutResult(
                    size_bytes=int(payload["size_bytes"]),
                    expires_at=payload.get("expires_at"),
                    ops_used=_maybe_int(resp.headers.get("x-hc-ops-used")),
                    ops_cap=_maybe_int(resp.headers.get("x-hc-ops-cap")),
                    ops_remaining=_maybe_int(resp.headers.get("x-hc-ops-remaining")),
                )
        except urllib.error.HTTPError as e:
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")

    def cache_get(self, fingerprint: str) -> Optional[bytes]:
        """Retrieve cached bytes for the given fingerprint.

        Returns:
            bytes on cache hit, None on cache miss (404 is the expected miss case).

        Raises:
            On other HTTP errors (401, 402, 429, 5xx) — typed exceptions.
        """
        req = urllib.request.Request(
            f"{self.base_url}/v1/cache/{fingerprint}",
            method="GET",
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "user-agent": f"hypercache-python/{__version__}",
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                return resp.read()
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None  # cache miss is not an error
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")

    def cache_lookup(
        self,
        data: Any,
        layers: int = DEFAULT_LAYERS,
        n_tok: int = DEFAULT_N_TOK,
    ) -> CacheLookupResult:
        """Compute the fingerprint of ``data`` AND check the cache in a single op.

        Saves a round trip versus calling ``fingerprint()`` then ``cache_get()``.

        Returns:
            CacheLookupResult. On hit, ``.value`` is the cached bytes. On miss,
            ``.value`` is None and ``.fingerprint_hex`` is the key you'd use
            with ``cache_put()`` to populate the cache.
        """
        body = _coerce_to_bytes(data)
        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "content-type": "application/octet-stream",
            "x-hc-layers": str(layers),
            "x-hc-n-tok": str(n_tok),
            "user-agent": f"hypercache-python/{__version__}",
        }
        req = urllib.request.Request(
            f"{self.base_url}/v1/cache/lookup",
            data=body,
            method="POST",
            headers=headers,
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                hit_header = resp.headers.get("x-hc-cache-hit", "0")
                fingerprint_hex = resp.headers.get("x-hc-fingerprint", "")
                ops_used = _maybe_int(resp.headers.get("x-hc-ops-used"))
                ops_cap = _maybe_int(resp.headers.get("x-hc-ops-cap"))
                ops_remaining = _maybe_int(resp.headers.get("x-hc-ops-remaining"))
                if hit_header == "1":
                    return CacheLookupResult(
                        hit=True,
                        fingerprint_hex=fingerprint_hex,
                        value=resp.read(),
                        expired=False,
                        ops_used=ops_used,
                        ops_cap=ops_cap,
                        ops_remaining=ops_remaining,
                    )
                # Miss path is JSON.
                payload = json.loads(resp.read())
                return CacheLookupResult(
                    hit=False,
                    fingerprint_hex=payload.get("fingerprint_hex", fingerprint_hex),
                    value=None,
                    expired=bool(payload.get("expired", False)),
                    ops_used=ops_used,
                    ops_cap=ops_cap,
                    ops_remaining=ops_remaining,
                )
        except urllib.error.HTTPError as e:
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")

    def cache_lookup_batch(
        self,
        inputs: list,  # list of bytes-like OR list of dicts with "data" and optional "prev"
        layers: int = DEFAULT_LAYERS,
        n_tok: int = DEFAULT_N_TOK,
    ) -> list:
        """Look up many records in a single round trip.

        Each item is fingerprinted and cache-checked atomically. Strict
        all-or-nothing on op accounting: if the batch would exceed your cap,
        nothing is charged and a QuotaError is raised with the current quota.

        Args:
            inputs: list of items. Each item is either:
                - raw bytes / numpy array / torch tensor / buffer-protocol object, OR
                - dict with keys "data" (the input), optionally "prev" (bytes or
                  hex string), optionally "layers", optionally "n_tok".
            layers: default layer count if item dict doesn't specify.
            n_tok: default token count if item dict doesn't specify.

        Returns:
            list[BatchLookupItem] in the same order as inputs.

        Raises:
            QuotaError on 402 (op cap would be exceeded; message includes current quota).
        """
        if not isinstance(inputs, list) or not inputs:
            raise ClientError("cache_lookup_batch: inputs must be a non-empty list")

        items_payload = []
        for i, item in enumerate(inputs):
            if isinstance(item, dict):
                data = item.get("data")
                if data is None:
                    raise ClientError(
                        f"cache_lookup_batch: inputs[{i}] dict missing 'data' key"
                    )
                data_bytes = _coerce_to_bytes(data)
                payload: dict = {
                    "data_b64": _b64encode(data_bytes),
                    "layers": int(item.get("layers", layers)),
                    "n_tok": int(item.get("n_tok", n_tok)),
                }
                prev = item.get("prev")
                if prev:
                    payload["prev_hex"] = _coerce_prev(prev)
                items_payload.append(payload)
            else:
                data_bytes = _coerce_to_bytes(item)
                items_payload.append({
                    "data_b64": _b64encode(data_bytes),
                    "layers": layers,
                    "n_tok": n_tok,
                })

        body = json.dumps({"items": items_payload}).encode("utf-8")
        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "content-type": "application/json",
            "user-agent": f"hypercache-python/{__version__}",
        }
        req = urllib.request.Request(
            f"{self.base_url}/v1/cache/lookup/batch",
            data=body,
            method="POST",
            headers=headers,
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                payload = json.loads(resp.read())
                results = []
                for r in payload.get("items", []):
                    value: Optional[bytes] = None
                    if r.get("hit") and "value_b64" in r:
                        value = _b64decode(r["value_b64"])
                    results.append(BatchLookupItem(
                        hit=bool(r.get("hit", False)),
                        fingerprint_hex=r.get("fingerprint_hex", ""),
                        value=value,
                        expired=bool(r.get("expired", False)),
                        size_bytes=r.get("size_bytes"),
                        stored_at=r.get("stored_at"),
                        expires_at=r.get("expires_at"),
                    ))
                return results
        except urllib.error.HTTPError as e:
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")

    def cached_embedding(
        self,
        model: str,
        text: str,
        compute: Any,  # Callable[[str], list[float]]
        ttl: Optional[int] = 86400,
    ) -> EmbeddingResult:
        """Wrap an embedding function with caching keyed by (model, text).

        Args:
            model: model identifier (e.g., "text-embedding-3-small"). Becomes
                   part of the cache key, so different models do not collide.
            text: input text whose embedding you want.
            compute: callable that takes the text and returns a list of floats.
                     Called only on cache miss.
            ttl: seconds to keep the cached embedding (default 24h).

        Returns:
            EmbeddingResult with the embedding vector and a ``hit`` flag.

        Cost model:
            - Hit: 1 op (the lookup).
            - Miss: 2 ops (lookup + put) + 1 call to your ``compute`` function.
            Caching pays off when the same (model, text) pair would be re-embedded
            often enough that 1 op << avoided provider cost × hit rate.
        """
        # Cache key derivation: model + \n + text, encoded as UTF-8.
        # The \n separator prevents accidental collisions between, e.g.,
        # model="a" text="b\nc" and model="a\nb" text="c".
        key_bytes = f"{model}\n{text}".encode("utf-8")

        lookup = self.cache_lookup(key_bytes)
        if lookup.hit and lookup.value is not None:
            try:
                embedding = json.loads(lookup.value.decode("utf-8"))
            except Exception as e:
                # Corrupt cache entry — fall through to recompute.
                embedding = None
            if isinstance(embedding, list):
                return EmbeddingResult(
                    embedding=embedding,
                    hit=True,
                    fingerprint_hex=lookup.fingerprint_hex,
                    ops_used=lookup.ops_used,
                    ops_remaining=lookup.ops_remaining,
                )

        # Miss (or corrupt hit) — call the user's compute function.
        embedding = compute(text)
        if not isinstance(embedding, list):
            # Coerce numpy arrays / tuples to plain list.
            try:
                embedding = list(embedding)
            except TypeError:
                raise ClientError(
                    "cached_embedding: compute() must return a list of floats "
                    f"(or convertible), got {type(embedding).__name__}"
                )

        payload = json.dumps(embedding).encode("utf-8")
        put_result = self.cache_put(lookup.fingerprint_hex, payload, ttl=ttl)
        return EmbeddingResult(
            embedding=embedding,
            hit=False,
            fingerprint_hex=lookup.fingerprint_hex,
            ops_used=put_result.ops_used,
            ops_remaining=put_result.ops_remaining,
        )

    def cache_delete(self, fingerprint: str) -> None:
        """Delete cached entry. Idempotent — never errors on already-deleted."""
        req = urllib.request.Request(
            f"{self.base_url}/v1/cache/{fingerprint}",
            method="DELETE",
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "user-agent": f"hypercache-python/{__version__}",
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                resp.read()
        except urllib.error.HTTPError as e:
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")

    # ---------- Organizational methods: list, relabel, bulk delete ----------

    def cache_list(
        self,
        bucket: str = "today",
        part: str = "ALL",
        run: Optional[str] = None,
        label_prefix: Optional[str] = None,
        limit: int = 100,
        cursor: Optional[int] = None,
    ) -> CacheListResponse:
        """List your cache entries filtered by time bucket + run + label prefix.

        Args:
            bucket: time window. One of: "today", "yesterday", "this-week",
                    "this-month", "this-year", "YYYY", "YYYY-MM", or "YYYY-MM-DD".
            part: time-of-day filter within the bucket. "AM" (00:00–11:59),
                  "PM" (12:00–23:59), or "ALL" (default).
            run: optional exact match on the run identifier.
            label_prefix: optional case-sensitive prefix match on the label.
            limit: max entries per response (default 100, max 500).
            cursor: pagination cursor returned from a previous call.

        Returns:
            CacheListResponse with entries grouped by run inside the bucket
            window. Use .next_cursor for the next page; None = no more pages.

        Cost: 0.25 weighted ops per call (D1 query, no R2 reads).
        """
        from urllib.parse import urlencode

        params: list = [("bucket", bucket), ("part", part), ("limit", str(limit))]
        if run is not None:
            params.append(("run", run))
        if label_prefix is not None:
            params.append(("label_prefix", label_prefix))
        if cursor is not None:
            params.append(("cursor", str(cursor)))
        qs = urlencode(params)

        req = urllib.request.Request(
            f"{self.base_url}/v1/cache/list?{qs}",
            method="GET",
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "user-agent": f"hypercache-python/{__version__}",
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                payload = json.loads(resp.read())
                runs = []
                for r in payload.get("runs", []):
                    entries = [
                        CacheListEntry(
                            fingerprint_hex=e["fingerprint_hex"],
                            label=e.get("label"),
                            run=e.get("run"),
                            size_bytes=int(e.get("size_bytes", 0)),
                            stored_at=int(e.get("stored_at", 0)),
                            expires_at=e.get("expires_at"),
                        )
                        for e in r.get("entries", [])
                    ]
                    runs.append(CacheListRun(
                        run=r.get("run"),
                        count=int(r.get("count", 0)),
                        total_bytes=int(r.get("total_bytes", 0)),
                        entries=entries,
                    ))
                return CacheListResponse(
                    bucket=payload.get("bucket", bucket),
                    part=payload.get("part", part),
                    total_count=int(payload.get("total_count", 0)),
                    total_bytes=int(payload.get("total_bytes", 0)),
                    runs=runs,
                    next_cursor=payload.get("next_cursor"),
                )
        except urllib.error.HTTPError as e:
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")

    def cache_relabel(
        self,
        fingerprint: str,
        label: Optional[str] = None,
        run: Optional[str] = None,
    ) -> RelabelResult:
        """Update the label and/or run of an existing cache entry without touching data.

        Pass an empty string or None to clear that field. At least one of
        label or run must be provided.

        Args:
            fingerprint: hex string of the entry to relabel.
            label: new label, or None to leave unchanged. Pass "" or `None` and
                   set the parameter explicitly to clear.
            run: new run, same semantics as label.

        Returns:
            RelabelResult with .relabeled=True on success, plus the new values.

        Raises:
            ClientError on 404 (no such entry) or 400 (invalid label/run).
        """
        if label is None and run is None:
            raise ClientError("cache_relabel: must provide label= or run=")
        body_dict: dict = {}
        if label is not None:
            body_dict["label"] = label if label != "" else None
        if run is not None:
            body_dict["run"] = run if run != "" else None
        body = json.dumps(body_dict).encode("utf-8")

        req = urllib.request.Request(
            f"{self.base_url}/v1/cache/{fingerprint}/relabel",
            data=body,
            method="POST",
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "content-type": "application/json",
                "user-agent": f"hypercache-python/{__version__}",
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                payload = json.loads(resp.read())
                return RelabelResult(
                    relabeled=bool(payload.get("relabeled", False)),
                    fingerprint_hex=payload.get("fingerprint_hex", fingerprint),
                    label=payload.get("label"),
                    run=payload.get("run"),
                )
        except urllib.error.HTTPError as e:
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")

    def cache_bulk_delete_by_label(
        self,
        label_prefix: str,
        confirm_count: int,
    ) -> BulkDeleteResult:
        """Delete every cache entry whose label starts with the given prefix.

        Two-step safety: you MUST first call cache_list(label_prefix=...) to
        learn the count of matching entries, then pass that exact integer as
        confirm_count. Mismatch returns 409 — no data is touched.

        Requires Starter tier or higher; lower tiers raise QuotaError (403).

        Args:
            label_prefix: prefix to match (e.g., "prod/song1/").
            confirm_count: the exact total_count from a prior cache_list call.

        Returns:
            BulkDeleteResult with deleted count and bytes_freed.

        Raises:
            ClientError on 409 if confirm_count doesn't match server's count.
            QuotaError on 403 if the tier doesn't have bulk-delete enabled.
        """
        from urllib.parse import urlencode
        qs = urlencode([("label_prefix", label_prefix), ("confirm", str(confirm_count))])
        req = urllib.request.Request(
            f"{self.base_url}/v1/cache/by-label?{qs}",
            method="DELETE",
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "user-agent": f"hypercache-python/{__version__}",
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                payload = json.loads(resp.read())
                return BulkDeleteResult(
                    deleted=int(payload.get("deleted", 0)),
                    bytes_freed=int(payload.get("bytes_freed", 0)),
                )
        except urllib.error.HTTPError as e:
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")

    def cache_bulk_delete_by_age(
        self,
        older_than: str,
        confirm_count: int,
    ) -> BulkDeleteResult:
        """Delete every cache entry older than the given relative time.

        Two-step safety: first call cache_list(bucket="...") to learn the count
        of matching entries, then pass that exact integer as confirm_count.

        Requires Starter tier or higher.

        Args:
            older_than: relative-time shorthand. Examples: "30d" (days),
                        "12h" (hours), "2w" (weeks), "1m" (months), "1y" (years).
            confirm_count: the exact count to delete from a prior list.

        Returns:
            BulkDeleteResult with deleted, bytes_freed, and cutoff_unix.

        Raises:
            ClientError on 409 if confirm_count doesn't match.
            QuotaError on 403 if the tier doesn't have bulk-delete enabled.
        """
        from urllib.parse import urlencode
        qs = urlencode([("older_than", older_than), ("confirm", str(confirm_count))])
        req = urllib.request.Request(
            f"{self.base_url}/v1/cache/by-age?{qs}",
            method="DELETE",
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "user-agent": f"hypercache-python/{__version__}",
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                payload = json.loads(resp.read())
                return BulkDeleteResult(
                    deleted=int(payload.get("deleted", 0)),
                    bytes_freed=int(payload.get("bytes_freed", 0)),
                    cutoff_unix=payload.get("cutoff_unix"),
                )
        except urllib.error.HTTPError as e:
            error_body = ""
            try:
                error_body = e.read().decode("utf-8", errors="replace")
            except Exception:
                pass
            _raise_for_status(e.code, error_body)
            raise
        except urllib.error.URLError as e:
            raise ServerError(f"Network error: {e.reason}")


def _b64encode(b: bytes) -> str:
    """base64 a bytes object, returning the str representation."""
    import base64
    return base64.b64encode(b).decode("ascii")


def _b64decode(s: str) -> bytes:
    """Decode a base64 str into bytes."""
    import base64
    return base64.b64decode(s)


# ---------- Session: chain-aware wrapper for agent loops ----------

class Session:
    """A chain-aware wrapper around a Client that auto-passes the previous
    record as ``prev`` on every subsequent fingerprint or cache_lookup call.

    Use this in agent loops where each step's record should link to the
    prior step's.

    Example:
        session = hypercache.Session()
        r1 = session.fingerprint(b"user query: where is the gate?")
        r2 = session.fingerprint(b"retrieval: gate is at concourse C")
        r3 = session.fingerprint(b"reply: your gate is at concourse C")
        # r1, r2, r3 are linked: r3's record encodes that r2 came before it,
        # r2's that r1 came before it.

    Reset the chain (start a new chain) with .reset().
    """

    def __init__(
        self,
        client: Optional[Client] = None,
        api_key: Optional[str] = None,
        run: Optional[str] = None,
    ):
        if client is None:
            client = Client(api_key=api_key)
        self.client = client
        self._prev: Optional[bytes] = None
        self._run: Optional[str] = run

    @property
    def prev(self) -> Optional[bytes]:
        """The most recent record, or None if no calls have been made."""
        return self._prev

    @property
    def run(self) -> Optional[str]:
        """The run name currently attached to PUTs in this session."""
        return self._run

    def reset(self) -> None:
        """Reset the chain. Subsequent calls start a fresh chain."""
        self._prev = None

    def with_run(self, run_name: str):
        """Context manager: auto-attach x-hc-run to every PUT inside the block.

        Example:
            session = hypercache.Session()
            with session.with_run("agent-abc/turn-5"):
                session.cache_put(fp, payload)
                session.cache_put(fp2, payload2)
            # Outside the with block, run is restored to whatever it was before.

        Nests cleanly; inner with_run() overrides outer for the inner scope.
        """
        from contextlib import contextmanager

        @contextmanager
        def _scope():
            old_run = self._run
            self._run = run_name
            try:
                yield self
            finally:
                self._run = old_run

        return _scope()

    def fingerprint(
        self,
        data: Any,
        layers: int = DEFAULT_LAYERS,
        n_tok: int = DEFAULT_N_TOK,
    ) -> FingerprintResult:
        """Fingerprint ``data``, auto-chained to the previous record in this session."""
        result = self.client.fingerprint(data, layers=layers, n_tok=n_tok, prev=self._prev)
        self._prev = result.record
        return result

    def cache_lookup(
        self,
        data: Any,
        layers: int = DEFAULT_LAYERS,
        n_tok: int = DEFAULT_N_TOK,
    ) -> CacheLookupResult:
        """Combined fingerprint + cache check, auto-chained to the previous record.

        Note: the chain advances even on cache hits (the fingerprint is still
        computed by the server using the chained ``prev``).
        """
        # cache_lookup doesn't currently take a prev argument in the Client API.
        # For chain advancement, we need the server to use prev — which the
        # /v1/cache/lookup endpoint accepts via the x-hc-prev header. Right now
        # the Client.cache_lookup method doesn't forward that. For v1 of Session,
        # we approximate: do a fingerprint with prev (updates chain), then look
        # up that fingerprint directly.
        fp = self.client.fingerprint(data, layers=layers, n_tok=n_tok, prev=self._prev)
        self._prev = fp.record
        cached = self.client.cache_get(fp.record_hex)
        return CacheLookupResult(
            hit=cached is not None,
            fingerprint_hex=fp.record_hex,
            value=cached,
            ops_used=fp.ops_used,
            ops_cap=fp.ops_cap,
            ops_remaining=fp.ops_remaining,
        )

    def cache_put(
        self,
        fingerprint_hex: str,
        data: Any,
        ttl: Optional[int] = None,
        label: Optional[str] = None,
        run: Optional[str] = None,
    ) -> CachePutResult:
        """Store data under the given fingerprint. Doesn't advance the chain.

        If ``run`` is None and the session has a run set (e.g., inside
        ``with session.with_run(...)``), that run is auto-attached.
        """
        effective_run = run if run is not None else self._run
        return self.client.cache_put(
            fingerprint_hex, data, ttl=ttl, label=label, run=effective_run
        )

    def cache_list(
        self,
        bucket: str = "today",
        part: str = "ALL",
        run: Optional[str] = None,
        label_prefix: Optional[str] = None,
        limit: int = 100,
        cursor: Optional[int] = None,
    ) -> CacheListResponse:
        """List cache entries. Forwards to Client.cache_list().

        If ``run`` is None and the session has a run set, filters by that run.
        Pass run="" to explicitly query unscoped entries.
        """
        effective_run = run if run is not None else self._run
        return self.client.cache_list(
            bucket=bucket, part=part, run=effective_run,
            label_prefix=label_prefix, limit=limit, cursor=cursor,
        )

    def cache_relabel(
        self,
        fingerprint_hex: str,
        label: Optional[str] = None,
        run: Optional[str] = None,
    ) -> RelabelResult:
        """Forward to Client.cache_relabel()."""
        return self.client.cache_relabel(fingerprint_hex, label=label, run=run)

    def cache_bulk_delete_by_label(
        self, label_prefix: str, confirm_count: int
    ) -> BulkDeleteResult:
        """Forward to Client.cache_bulk_delete_by_label()."""
        return self.client.cache_bulk_delete_by_label(label_prefix, confirm_count)

    def cache_bulk_delete_by_age(
        self, older_than: str, confirm_count: int
    ) -> BulkDeleteResult:
        """Forward to Client.cache_bulk_delete_by_age()."""
        return self.client.cache_bulk_delete_by_age(older_than, confirm_count)

    def cached_embedding(
        self,
        model: str,
        text: str,
        compute: Any,
        ttl: Optional[int] = 86400,
    ) -> EmbeddingResult:
        """Cached embedding via the wrapped client. Does NOT advance the chain —
        embedding caching has its own keying (model + text) that's independent
        of the session's chain.
        """
        return self.client.cached_embedding(model, text, compute, ttl=ttl)


def _maybe_num(s: Optional[str]) -> Optional[float]:
    # Weighted ops are fractional (e.g. a cache hit is 1.25), so parse as float.
    # int() would raise on "1.25" and (caught) yield None, silently dropping the value.
    if s is None:
        return None
    try:
        return float(s)
    except (TypeError, ValueError):
        return None


# Back-compat alias: older code referenced _maybe_int.
_maybe_int = _maybe_num


# ---------- Module-level convenience ----------

_default_client: Optional[Client] = None


def fingerprint(
    data: Any,
    layers: int = DEFAULT_LAYERS,
    n_tok: int = DEFAULT_N_TOK,
    prev: Optional[Union[bytes, bytearray, str]] = None,
    api_key: Optional[str] = None,
) -> FingerprintResult:
    """Module-level shortcut for the common case.

    Lazily constructs a default Client using HYPERCACHE_KEY from the environment
    on first call. Pass api_key= to use a one-off key without affecting the default.
    """
    global _default_client
    if api_key is not None:
        # Fresh client per call when api_key is explicit (no shared state)
        return Client(api_key=api_key).fingerprint(
            data, layers=layers, n_tok=n_tok, prev=prev
        )
    if _default_client is None:
        _default_client = Client()
    return _default_client.fingerprint(data, layers=layers, n_tok=n_tok, prev=prev)


def cache_put(
    fingerprint: str,
    data: Any,
    ttl: Optional[int] = None,
    api_key: Optional[str] = None,
) -> CachePutResult:
    """Module-level cache_put shortcut."""
    global _default_client
    if api_key is not None:
        return Client(api_key=api_key).cache_put(fingerprint, data, ttl=ttl)
    if _default_client is None:
        _default_client = Client()
    return _default_client.cache_put(fingerprint, data, ttl=ttl)


def cache_get(fingerprint: str, api_key: Optional[str] = None) -> Optional[bytes]:
    """Module-level cache_get shortcut. Returns None on miss."""
    global _default_client
    if api_key is not None:
        return Client(api_key=api_key).cache_get(fingerprint)
    if _default_client is None:
        _default_client = Client()
    return _default_client.cache_get(fingerprint)


def cache_delete(fingerprint: str, api_key: Optional[str] = None) -> None:
    """Module-level cache_delete shortcut."""
    global _default_client
    if api_key is not None:
        Client(api_key=api_key).cache_delete(fingerprint)
        return
    if _default_client is None:
        _default_client = Client()
    _default_client.cache_delete(fingerprint)


def cache_lookup(
    data: Any,
    layers: int = DEFAULT_LAYERS,
    n_tok: int = DEFAULT_N_TOK,
    api_key: Optional[str] = None,
) -> CacheLookupResult:
    """Module-level cache_lookup shortcut. Combined fingerprint + cache check (1 op)."""
    global _default_client
    if api_key is not None:
        return Client(api_key=api_key).cache_lookup(data, layers=layers, n_tok=n_tok)
    if _default_client is None:
        _default_client = Client()
    return _default_client.cache_lookup(data, layers=layers, n_tok=n_tok)


def cache_lookup_batch(
    inputs: list,
    layers: int = DEFAULT_LAYERS,
    n_tok: int = DEFAULT_N_TOK,
    api_key: Optional[str] = None,
) -> list:
    """Module-level batch lookup shortcut. Many records, one round trip.

    Example:
        results = hypercache.cache_lookup_batch([b"item one", b"item two", b"item three"])
        for r in results:
            print("hit" if r.hit else "miss", r.fingerprint_hex)
    """
    global _default_client
    if api_key is not None:
        return Client(api_key=api_key).cache_lookup_batch(inputs, layers=layers, n_tok=n_tok)
    if _default_client is None:
        _default_client = Client()
    return _default_client.cache_lookup_batch(inputs, layers=layers, n_tok=n_tok)


def cached_embedding(
    model: str,
    text: str,
    compute: Any,  # Callable[[str], list[float]]
    ttl: Optional[int] = 86400,
    api_key: Optional[str] = None,
) -> EmbeddingResult:
    """Module-level cached_embedding shortcut.

    Example:
        from openai import OpenAI
        import hypercache

        client = OpenAI()

        def embed(text: str) -> list[float]:
            return client.embeddings.create(
                model="text-embedding-3-small", input=text
            ).data[0].embedding

        result = hypercache.cached_embedding(
            model="text-embedding-3-small",
            text="The quick brown fox",
            compute=embed,
        )
        print(result.embedding[:4], "hit=", result.hit)
    """
    global _default_client
    if api_key is not None:
        return Client(api_key=api_key).cached_embedding(model, text, compute, ttl=ttl)
    if _default_client is None:
        _default_client = Client()
    return _default_client.cached_embedding(model, text, compute, ttl=ttl)
