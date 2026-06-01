# hypercache (Rust SDK)

Rust client for the [HyperCache](https://hypercache.ai) API. Blocking — no async runtime needed.

## Add to your project

```toml
[dependencies]
hypercache-kv = "0.1"
```

The library name is `hypercache`, so your code uses `use hypercache::Client;` regardless of the crates.io package name.

```bash
export HYPERCACHE_KEY=hck_...
```

## Pipeline

```rust
use hypercache::{Client, Pipeline};

let client = Client::new()?;
let mut p = Pipeline::new(client, "my_pipeline");

let (answer, was_hit) = p.cached(
    "gpt_call",
    prompt.as_bytes(),
    || call_openai(prompt),
    None,
)?;

p.record("output", answer.as_bytes())?;

let report = p.end();
println!("{} hits / {} misses", report.n_hits(), report.n_misses());
```

## Cache an expensive call

```rust
let lookup = client.cache_lookup(b"Translate: Hello")?;
if lookup.hit {
    println!("cached: {:?}", lookup.value);
} else {
    let response = call_openai("Translate: Hello")?;
    client.cache_put(&lookup.fingerprint_hex, response.as_bytes(), Some(3600))?;
}
```

## Records

```rust
let fp = client.fingerprint(some_bytes)?;
println!("{}", fp.record_hex);

client.cache_put(&fp.record_hex, expensive_output, Some(3600))?;
let cached = client.cache_get(&fp.record_hex)?;
```

Link records to a prior one:

```rust
let mut session = hypercache::Session::new(client);
let r1 = session.fingerprint(input_bytes)?;
let r2 = session.fingerprint(model_output)?;
let r3 = session.fingerprint(reviewer_note)?;
```

## License

MIT. See [LICENSE](./LICENSE).
