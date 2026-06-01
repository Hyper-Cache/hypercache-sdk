# hypercache (Go SDK)

Go client for the [HyperCache](https://hypercache.ai) API. Standard library only — zero external dependencies.

## Install

```bash
go get github.com/Hyper-Cache/hypercache-sdk/sdks/go@latest
export HYPERCACHE_KEY=hck_...
```

## Pipeline

```go
import "github.com/Hyper-Cache/hypercache-sdk/sdks/go"

client, _ := hypercache.NewClient()
p := hypercache.NewPipeline(client, "my_pipeline")
defer p.End()

answer, wasHit, _ := p.Cached(ctx, "gpt_call",
    []byte(prompt),
    func() (string, error) { return callOpenAI(prompt) },
)
p.Record(ctx, "output", []byte(answer))

report := p.Report()
fmt.Printf("%d hits / %d misses\n", report.NHits(), report.NMisses())
```

## Cache an expensive call

```go
results, err := client.CacheLookupBatch(ctx, []hypercache.BatchInput{
    {Data: []byte("prompt one")},
    {Data: []byte("prompt two")},
})
for _, r := range results {
    if r.Hit {
        fmt.Println("cached:", string(r.Value))
    }
}
```

## Records

```go
fp, _ := client.Fingerprint(ctx, someBytes)
fmt.Println(fp.RecordHex)

client.CachePut(ctx, fp.RecordHex, expensiveOutputBytes, hypercache.WithTTL(3600))
cached, _ := client.CacheGet(ctx, fp.RecordHex)
```

Link records to a prior one:

```go
session := hypercache.NewSession(client)
r1, _ := session.Fingerprint(ctx, inputBytes)
r2, _ := session.Fingerprint(ctx, modelOutput)
r3, _ := session.Fingerprint(ctx, reviewerNote)
```

## License

MIT. See [LICENSE](./LICENSE).
