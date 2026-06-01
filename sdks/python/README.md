# hypercache (Python SDK)

Python client for the [HyperCache](https://hypercache.ai) API. Zero runtime dependencies (stdlib only).

## Install

```bash
pip install hypercache-kv
export HYPERCACHE_KEY=hck_...
```

Get a key at [hypercache.ai](https://hypercache.ai).

## Pipeline

Cache, records, and stats in one context:

```python
from hypercache.workflows import Pipeline

with Pipeline("my_pipeline") as p:
    answer, was_hit = p.cached(
        label="gpt_call",
        input_bytes=prompt.encode("utf-8"),
        compute=lambda: call_openai(prompt),
    )
    p.record("output", answer.encode("utf-8"))

print(f"{p.report.n_hits} hits / {p.report.n_misses} misses")
```

## Cache an expensive call

```python
import hypercache

result = hypercache.cache_lookup(b"some input bytes")
if result.hit:
    print(result.value)
else:
    hypercache.cache_put(result.fingerprint_hex, b"my expensive output", ttl=3600)

results = hypercache.cache_lookup_batch([b"in 1", b"in 2", b"in 3"])
```

## Wrap your LLM client

```python
from openai import OpenAI
from hypercache.workflows import wrap_openai

client = wrap_openai(OpenAI())
resp = client.chat.completions.create(model="gpt-4o-mini", messages=[...])
```

`wrap_anthropic` does the same for the Anthropic SDK.

## Records

```python
import hypercache

fp = hypercache.fingerprint(b"any bytes")
print(fp.record_hex)
```

Link records to a prior one:

```python
from hypercache.workflows import audit_chain

with audit_chain() as chain:
    r1 = chain.fingerprint(input_bytes)
    r2 = chain.fingerprint(model_output)
    r3 = chain.fingerprint(reviewer_note)
```

## License

MIT. See [LICENSE](./LICENSE).
