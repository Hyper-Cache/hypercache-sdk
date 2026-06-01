"""
hypercache.workflows — helper patterns for common workflows.


Patterns:
    Pipeline(name)                        — cache + records + stats in one context
    cached_completion(prompt, compute)    — cache repeated LLM calls
    audit_chain()                         — record each step
    wrap_openai(client)                   — cached OpenAI client
    wrap_anthropic(client)                — cached Anthropic client
"""

from __future__ import annotations

import time
from contextlib import contextmanager
from dataclasses import dataclass, field
from typing import Any, Callable, Iterator, Optional

from . import Client, Session


# ---------- Pattern 0: Pipeline — ALL-IN template ---------- #

@dataclass
class PipelineStep:
    """One entry in a pipeline's chain. Records what happened at each step."""

    label: str
    fingerprint_hex: str
    was_cache_hit: Optional[bool] = None     # None = pure record, no compute
    elapsed_ms: float = 0.0
    bytes_processed: int = 0


@dataclass
class PipelineReport:
    """End-of-pipeline summary. Useful for dashboards and audit export."""

    name: str
    steps: list[PipelineStep] = field(default_factory=list)
    started_at: float = 0.0
    ended_at: float = 0.0

    @property
    def n_steps(self) -> int:
        return len(self.steps)

    @property
    def n_hits(self) -> int:
        return sum(1 for s in self.steps if s.was_cache_hit is True)

    @property
    def n_misses(self) -> int:
        return sum(1 for s in self.steps if s.was_cache_hit is False)

    @property
    def total_seconds(self) -> float:
        return max(0.0, self.ended_at - self.started_at)

    @property
    def chain(self) -> list[tuple[str, str]]:
        """The audit chain: ordered (label, fingerprint_hex) pairs."""
        return [(s.label, s.fingerprint_hex) for s in s_iter(self.steps)]

    def export_audit(self) -> list[dict]:
        """JSON-serializable chain export for compliance/auditor handoff."""
        return [
            {
                "label": s.label,
                "fingerprint_hex": s.fingerprint_hex,
                "cache_hit": s.was_cache_hit,
                "elapsed_ms": round(s.elapsed_ms, 2),
                "bytes": s.bytes_processed,
            }
            for s in self.steps
        ]


def s_iter(steps):
    yield from steps


class Pipeline:
    """A single context for caching, records, and stats.

    Inside a Pipeline you get:
      - ``cached(label, input_bytes, compute)`` — cache an expensive call
      - ``record(label, bytes)``                — record any step
      - ``report`` (after exit)                 — stats + chain

    Same inputs across runs → cache hits → faster, cheaper. Each record
    links to the prior one. The
    ``PipelineReport`` returned at exit holds the chain you'd hand an
    auditor and the stats you'd show a dashboard.

    Example:
        from hypercache.workflows import Pipeline
        from openai import OpenAI
        openai = OpenAI()

        with Pipeline("translate_user_message") as p:
            p.record("input", user_msg.encode("utf-8"))

            translation, was_hit = p.cached(
                label="gpt_translate",
                input_bytes=f"Translate to French: {user_msg}".encode("utf-8"),
                compute=lambda: openai.chat.completions.create(
                    model="gpt-4o-mini",
                    messages=[{"role":"user","content":f"Translate to French: {user_msg}"}]
                ).choices[0].message.content
            )

            p.record("output", translation.encode("utf-8"))

        # After the block:
        print(f"{p.report.n_hits} hits, {p.report.n_misses} misses, "
              f"chain length {p.report.n_steps}")
        audit_doc = p.report.export_audit()   # hand to compliance
    """

    def __init__(self, name: str, client: Optional[Client] = None):
        self.name = name
        self._client = client or Client()
        self._session = Session(client=self._client)
        self.report: PipelineReport = PipelineReport(name=name)

    def __enter__(self) -> "Pipeline":
        self.report.started_at = time.perf_counter()
        return self

    def __exit__(self, exc_type, exc_val, exc_tb) -> None:
        self.report.ended_at = time.perf_counter()

    # ---- record: add a step to the chain (no caching) ----

    def record(self, label: str, data: Any) -> PipelineStep:
        """Add a step to the chain. Used for inputs, outputs, and intermediate
        states you want in the audit record but aren't trying to cache."""
        t0 = time.perf_counter()
        result = self._session.fingerprint(data)
        elapsed = (time.perf_counter() - t0) * 1000
        size = len(data) if isinstance(data, (bytes, bytearray, memoryview)) else 0
        step = PipelineStep(
            label=label,
            fingerprint_hex=result.record_hex,
            was_cache_hit=None,
            elapsed_ms=elapsed,
            bytes_processed=size,
        )
        self.report.steps.append(step)
        return step

    # ---- cached: skip work on repeated inputs ----

    def cached(
        self,
        label: str,
        input_bytes: bytes | str,
        compute: Callable[[], Any],
        *,
        ttl: int = 86400,
    ) -> tuple[Any, bool]:
        """Cache an expensive computation by its input bytes.

        Same input bytes next time → cached result, no compute call. New input
        → calls ``compute()``, stores its return value (as bytes via str/repr)
        keyed by the input's fingerprint.

        Returns:
            (result, was_cache_hit)
        """
        t0 = time.perf_counter()
        if isinstance(input_bytes, str):
            input_bytes = input_bytes.encode("utf-8")

        lookup = self._client.cache_lookup(input_bytes)
        # Advance the chain even on a hit so cached steps still appear in order.
        self._session._prev = bytes.fromhex(lookup.fingerprint_hex)

        if lookup.hit and lookup.value is not None:
            elapsed = (time.perf_counter() - t0) * 1000
            cached_text = lookup.value.decode("utf-8", errors="replace")
            step = PipelineStep(
                label=label,
                fingerprint_hex=lookup.fingerprint_hex,
                was_cache_hit=True,
                elapsed_ms=elapsed,
                bytes_processed=len(input_bytes),
            )
            self.report.steps.append(step)
            return cached_text, True

        # Miss — compute and store.
        result = compute()
        result_bytes = (
            result.encode("utf-8") if isinstance(result, str)
            else result if isinstance(result, (bytes, bytearray))
            else str(result).encode("utf-8")
        )
        self._client.cache_put(lookup.fingerprint_hex, result_bytes, ttl=ttl)
        elapsed = (time.perf_counter() - t0) * 1000
        step = PipelineStep(
            label=label,
            fingerprint_hex=lookup.fingerprint_hex,
            was_cache_hit=False,
            elapsed_ms=elapsed,
            bytes_processed=len(input_bytes),
        )
        self.report.steps.append(step)
        return result, False


# ---------- Pattern 1: cached_completion ---------- #

def cached_completion(
    prompt: str,
    compute: Callable[[str], str],
    *,
    ttl: int = 86400,
    client: Optional[Client] = None,
) -> tuple[str, bool]:
    """Run an LLM call with transparent caching.

    Same prompt in → cached response back. New prompt → calls ``compute(prompt)``,
    stores the response, returns it.

    Args:
        prompt: the input string sent to your LLM.
        compute: a function that takes the prompt and returns the LLM response.
                 Called only on cache miss.
        ttl: seconds to keep the cached response (default 24h).
        client: optional Client; constructed from HYPERCACHE_KEY env var if omitted.

    Returns:
        (response_text, was_cache_hit)

    Example:
        from openai import OpenAI
        from hypercache.workflows import cached_completion

        openai_client = OpenAI()

        def call_gpt(prompt: str) -> str:
            return openai_client.chat.completions.create(
                model="gpt-4o-mini",
                messages=[{"role": "user", "content": prompt}],
            ).choices[0].message.content

        text, was_hit = cached_completion("Translate hello to French", call_gpt)
    """
    if client is None:
        client = Client()
    prompt_bytes = prompt.encode("utf-8")
    lookup = client.cache_lookup(prompt_bytes)
    if lookup.hit and lookup.value is not None:
        return lookup.value.decode("utf-8"), True

    response = compute(prompt)
    client.cache_put(lookup.fingerprint_hex, response.encode("utf-8"), ttl=ttl)
    return response, False


# ---------- Pattern 2: audit_chain ---------- #

@contextmanager
def audit_chain(client: Optional[Client] = None) -> Iterator[Session]:
    """Open a chain-aware session that records every fingerprinted step with
    automatic ``prev`` linkage.

    Use as a context manager so each run gets its own chain.

    Example:
        from hypercache.workflows import audit_chain

        with audit_chain() as chain:
            r1 = chain.fingerprint(input_bytes)       # step 1
            r2 = chain.fingerprint(model_output)      # step 2, linked to r1
            r3 = chain.fingerprint(reviewer_note)     # step 3, linked to r2
            chain_records = [r1.record_hex, r2.record_hex, r3.record_hex]

        # Hand chain_records + the original input bytes to an auditor.
        # They re-run the fingerprints via the same API and verify the chain.
    """
    sess = Session(client=client)
    try:
        yield sess
    finally:
        # Nothing to clean up — chain records are already on the wire.
        pass


# ---------- Pattern 3: provider wrappers ---------- #

def wrap_openai(openai_client: Any, *, ttl: int = 86400, client: Optional[Client] = None) -> Any:
    """Return a wrapper around an OpenAI client whose ``chat.completions.create``
    method caches by request body. Same request body → cached response.

    The wrapper exposes the same interface as the OpenAI client for completions;
    other methods are passed through unchanged.

    Example:
        from openai import OpenAI
        from hypercache.workflows import wrap_openai

        client = wrap_openai(OpenAI())
        resp = client.chat.completions.create(
            model="gpt-4o-mini",
            messages=[{"role": "user", "content": "Hello"}],
        )
    """
    return _CachedOpenAI(openai_client, ttl=ttl, hc_client=client or Client())


def wrap_anthropic(anthropic_client: Any, *, ttl: int = 86400, client: Optional[Client] = None) -> Any:
    """Same as wrap_openai but for the Anthropic SDK's ``messages.create`` method."""
    return _CachedAnthropic(anthropic_client, ttl=ttl, hc_client=client or Client())


# ---------- Internal implementations ---------- #

import json


class _CachedOpenAI:
    def __init__(self, openai_client: Any, *, ttl: int, hc_client: Client):
        self._openai = openai_client
        self._ttl = ttl
        self._hc = hc_client
        self.chat = _CachedOpenAIChat(self)

    def __getattr__(self, name: str) -> Any:
        # Pass-through for everything we don't wrap.
        return getattr(self._openai, name)


class _CachedOpenAIChat:
    def __init__(self, parent: _CachedOpenAI):
        self._parent = parent
        self.completions = _CachedOpenAICompletions(parent)

    def __getattr__(self, name: str) -> Any:
        return getattr(self._parent._openai.chat, name)


class _CachedOpenAICompletions:
    def __init__(self, parent: _CachedOpenAI):
        self._parent = parent

    def create(self, **kwargs: Any) -> Any:
        # Streaming requests bypass caching (we'd need to buffer the stream).
        if kwargs.get("stream"):
            return self._parent._openai.chat.completions.create(**kwargs)

        # Build a stable cache key from the request body.
        # Sort keys so semantically-equal requests produce the same fingerprint.
        body = json.dumps(kwargs, sort_keys=True, default=str).encode("utf-8")
        lookup = self._parent._hc.cache_lookup(body)
        if lookup.hit and lookup.value is not None:
            # Cached — return the deserialized completion.
            cached_json = json.loads(lookup.value.decode("utf-8"))
            return _DictResponse(cached_json)

        # Miss — call OpenAI, cache the response.
        resp = self._parent._openai.chat.completions.create(**kwargs)
        # Serialize using model_dump if available (Pydantic v2), else dict().
        if hasattr(resp, "model_dump"):
            resp_json = resp.model_dump()
        elif hasattr(resp, "dict"):
            resp_json = resp.dict()
        else:
            resp_json = dict(resp)
        self._parent._hc.cache_put(
            lookup.fingerprint_hex,
            json.dumps(resp_json).encode("utf-8"),
            ttl=self._parent._ttl,
        )
        return resp


class _CachedAnthropic:
    def __init__(self, anthropic_client: Any, *, ttl: int, hc_client: Client):
        self._anthropic = anthropic_client
        self._ttl = ttl
        self._hc = hc_client
        self.messages = _CachedAnthropicMessages(self)

    def __getattr__(self, name: str) -> Any:
        return getattr(self._anthropic, name)


class _CachedAnthropicMessages:
    def __init__(self, parent: _CachedAnthropic):
        self._parent = parent

    def create(self, **kwargs: Any) -> Any:
        if kwargs.get("stream"):
            return self._parent._anthropic.messages.create(**kwargs)
        body = json.dumps(kwargs, sort_keys=True, default=str).encode("utf-8")
        lookup = self._parent._hc.cache_lookup(body)
        if lookup.hit and lookup.value is not None:
            cached_json = json.loads(lookup.value.decode("utf-8"))
            return _DictResponse(cached_json)
        resp = self._parent._anthropic.messages.create(**kwargs)
        if hasattr(resp, "model_dump"):
            resp_json = resp.model_dump()
        elif hasattr(resp, "dict"):
            resp_json = resp.dict()
        else:
            resp_json = dict(resp)
        self._parent._hc.cache_put(
            lookup.fingerprint_hex,
            json.dumps(resp_json).encode("utf-8"),
            ttl=self._parent._ttl,
        )
        return resp


class _DictResponse:
    """Light wrapper around a dict so cached responses access fields the same
    way Pydantic objects do (response.choices[0].message.content style)."""

    def __init__(self, data: dict):
        self._data = data
        for k, v in data.items():
            if isinstance(v, dict):
                setattr(self, k, _DictResponse(v))
            elif isinstance(v, list):
                setattr(self, k, [_DictResponse(i) if isinstance(i, dict) else i for i in v])
            else:
                setattr(self, k, v)

    def __getitem__(self, key: str) -> Any:
        return self._data[key]

    def __repr__(self) -> str:
        return f"_DictResponse({self._data!r})"
