# HyperCache SDKs

Official client libraries for the [HyperCache](https://hypercache.ai) API — Python, TypeScript/JavaScript, Rust, Go, and .NET.

## Three gains, one primitive

| Gain | What it does | Measured |
|---|---|---|
| **Skip repeated LLM calls** | Same prompt → cached response in milliseconds | **7.6×** faster on cache hit vs real Phi-3-mini |
| **Skip repeated GPU prefill** | Self-hosted inference reuses prefilled KV state | **21.8×** faster vs cold prefill at 1199 tokens |
| **Prove what happened** | Every step gets a cryptographic fingerprint, chained for audit | Byte-precision; forgery-resistant |

## Install

| Language | Install | Package |
|---|---|---|
| Python | `pip install hypercache-kv` | [PyPI](https://pypi.org/project/hypercache-kv/) |
| TypeScript / JS | `npm install hypercache-kv` | [npm](https://www.npmjs.com/package/hypercache-kv) |
| Rust | `cargo add hypercache-kv` | [crates.io](https://crates.io/crates/hypercache-kv) |
| Go | `go get github.com/Hyper-Cache/hypercache-sdk/sdks/go@latest` | — |
| .NET | `dotnet add package HyperCache.Sdk` | [NuGet](https://www.nuget.org/packages/HyperCache.Sdk/) |

Set your key:

```bash
export HYPERCACHE_KEY=hck_...
```

Get a key at [hypercache.ai](https://hypercache.ai).

## Quickstart (Python)

```python
import hypercache

result = hypercache.cache_lookup(b"some input")
if result.hit:
    use(result.value)
else:
    hypercache.cache_put(result.fingerprint_hex, expensive_output, ttl=3600)
```

Each language's README has full usage.

## Repository layout

```
sdks/
  python/      pip install hypercache-kv          (import: hypercache)
  typescript/  npm install hypercache-kv
  rust/        cargo add hypercache-kv            (lib: hypercache)
  go/          go get github.com/Hyper-Cache/hypercache-sdk/sdks/go
  dotnet/      dotnet add package HyperCache.Sdk  (namespace: HyperCache)
```

## License

MIT.
