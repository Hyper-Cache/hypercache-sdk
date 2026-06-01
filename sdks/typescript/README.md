# hypercache (TypeScript / JavaScript SDK)

JavaScript client for the [HyperCache](https://hypercache.ai) API. Zero runtime dependencies. Works in Node 18+, Deno, Bun, and modern browsers.

## Install

```bash
npm install hypercache-kv
export HYPERCACHE_KEY=hck_...
```

## Pipeline

```javascript
import { Pipeline } from "hypercache-kv";

const p = new Pipeline("my_pipeline");

const [answer, wasHit] = await p.cached(
  "gpt_call",
  new TextEncoder().encode(prompt),
  () => callOpenAI(prompt)
);

await p.record("output", new TextEncoder().encode(answer));

p.end();
console.log(`${p.report.nHits} hits / ${p.report.nMisses} misses`);
```

## Cache an expensive call

```javascript
import { Client } from "hypercache-kv";

const client = new Client();
const lookup = await client.cacheLookup(new TextEncoder().encode("Translate: Hello"));
if (lookup.hit) {
  console.log(new TextDecoder().decode(lookup.value));
} else {
  const response = await callOpenAI("Translate: Hello");
  await client.cachePut(lookup.fingerprintHex, new TextEncoder().encode(response));
}
```

## Records

```javascript
import { fingerprint, cachePut, cacheGet } from "hypercache-kv";

const fp = await fingerprint(someBytes);
console.log(fp.recordHex);

await cachePut(fp.recordHex, expensiveOutputBytes, { ttl: 3600 });
const cached = await cacheGet(fp.recordHex);
```

Link records to a prior one:

```javascript
import { Session } from "hypercache-kv";

const chain = new Session();
const r1 = await chain.fingerprint(inputBytes);
const r2 = await chain.fingerprint(modelOutput);
const r3 = await chain.fingerprint(reviewerNote);
```

## License

MIT. See [LICENSE](./LICENSE).
