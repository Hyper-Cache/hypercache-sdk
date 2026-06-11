# HyperCache.Sdk

An idiomatic C#/.NET SDK for the [HyperCache](https://hypercache.ai) API.

`HyperCache.Sdk` provides a fully async, Task-based client for generating
fingerprints, reading and writing cache values, organizing entries, and composing
chain-aware workflows. It uses only native .NET assemblies — `System.Net.Http`
for transport and `System.Text.Json` for serialization — with no third-party
runtime dependencies.

## Installation

```bash
dotnet add package HyperCache.Sdk
```

## Target frameworks

```text
netstandard2.0
net8.0
```

The `netstandard2.0` target provides broad compatibility with older .NET
consumers, including .NET Framework-era applications. The `net8.0` target enables
modern runtime behavior and optimizations.

## Configuration

Set your API key (and optionally a custom base URL) via environment variables:

```text
HYPERCACHE_KEY        # API key used to authenticate requests
HYPERCACHE_BASE_URL   # optional override for the API base URL
```

With those set, the parameterless constructor reads them automatically:

```csharp
using HyperCache;

// Reads HYPERCACHE_KEY, and HYPERCACHE_BASE_URL when present.
using var client = new Client();
```

Or configure the client explicitly:

```csharp
using System;
using HyperCache;

using var client = new Client(new HyperCacheClientOptions
{
    ApiKey = "your-api-key",
    BaseUrl = new Uri("https://api.hypercache.ai"),
    Timeout = TimeSpan.FromSeconds(30),
});
```

Configuration resolution rules:

- An explicit `HyperCacheClientOptions.ApiKey` always overrides `HYPERCACHE_KEY`.
- An explicit `HyperCacheClientOptions.BaseUrl` always overrides `HYPERCACHE_BASE_URL`.
- If no API key can be resolved from either the options or `HYPERCACHE_KEY`, the
  constructor throws `AuthException` immediately rather than creating an
  unauthenticated client.

The default base URL is `https://api.hypercache.ai`.

> **Reuse the client.** `Client` wraps an `HttpClient` and is safe to share across
> requests and threads. Create one `Client` (or use the static `HyperCacheClient`
> convenience methods, which share a single default client) and reuse it for the
> lifetime of your application rather than constructing a new one per request.

## Fingerprint

```csharp
using System.Text;
using HyperCache;

using var client = new Client();

FingerprintResult fp = await client.FingerprintAsync(Encoding.UTF8.GetBytes("hello world"));

Console.WriteLine(fp.RecordHex);
Console.WriteLine(fp.OpsUsed); // fractional quota usage (double?), e.g. 1.25
```

## Cache put and get

```csharp
using System.Text;
using HyperCache;

using var client = new Client();

FingerprintResult fp = await client.FingerprintAsync("some input");

CachePutResult put = await client.CachePutAsync(
    fp.RecordHex,
    Encoding.UTF8.GetBytes("expensive output"),
    new CachePutOptions { Ttl = 3600, Label = "demo" });

// CacheGetAsync returns null on a cache miss (HTTP 404 is treated as a miss, not an error).
byte[]? value = await client.CacheGetAsync(fp.RecordHex);
if (value is not null)
{
    Console.WriteLine(Encoding.UTF8.GetString(value));
}

await client.CacheDeleteAsync(fp.RecordHex); // idempotent
```

## Cache lookup

```csharp
using System.Text;
using HyperCache;

using var client = new Client();

CacheLookupResult result = await client.CacheLookupAsync(Encoding.UTF8.GetBytes("some input"));
if (result.Hit)
{
    Console.WriteLine(Encoding.UTF8.GetString(result.Value!));
}
else
{
    await client.CachePutAsync(result.FingerprintHex, Encoding.UTF8.GetBytes("computed"));
}
```

## Batch lookup

```csharp
using HyperCache;

using var client = new Client();

var results = await client.CacheLookupBatchAsync(new[]
{
    new CacheLookupBatchItem { Data = System.Text.Encoding.UTF8.GetBytes("input-1") },
    new CacheLookupBatchItem { Data = System.Text.Encoding.UTF8.GetBytes("input-2") },
});

foreach (BatchLookupResult item in results) // results preserve input order
{
    Console.WriteLine($"{item.FingerprintHex}: hit={item.Hit}");
}
```

## Cache list

```csharp
using HyperCache;

using var client = new Client();

CacheListResponse list = await client.CacheListAsync(new CacheListOptions
{
    Bucket = "today",
    Part = "ALL",
    Limit = 100,
    LabelPrefix = "demo",
});

foreach (CacheListRunGroup group in list.Runs)
{
    Console.WriteLine($"run={group.Run} count={group.Count}");
    foreach (CacheListEntry entry in group.Entries)
    {
        Console.WriteLine($"  {entry.FingerprintHex} ({entry.SizeBytes} bytes)");
    }
}

if (list.NextCursor is int cursor)
{
    // Pass cursor back via CacheListOptions.Cursor to fetch the next page.
}
```

## Relabel

```csharp
using HyperCache;

using var client = new Client();

// Set a new label and run.
await client.CacheRelabelAsync(fingerprint, new CacheRelabelOptions
{
    Label = "reviewed",
    Run = "run-42",
});

// A null value explicitly clears that field.
await client.CacheRelabelAsync(fingerprint, new CacheRelabelOptions
{
    Label = null,
    Run = null,
});
```

## Bulk delete (use with caution)

> Bulk deletes are destructive. They use a two-step confirm: list the matching
> entries first to learn the exact count, then pass that count as the confirm
> argument. Available on the Starter tier and above.

```csharp
using HyperCache;

using var client = new Client();

// Delete every entry whose label starts with the prefix.
BulkDeleteResult byLabel = await client.CacheBulkDeleteByLabelAsync("temp/", confirmCount: 12);

// Delete every entry older than a relative age (for example, 30d, 12h, 1y).
BulkDeleteResult byAge = await client.CacheBulkDeleteByAgeAsync("30d", confirmCount: 5);

Console.WriteLine($"{byLabel.Deleted} deleted, {byLabel.BytesFreed} bytes freed");
```

## Session (chain-aware)

A `Session` threads the previous fingerprint into subsequent fingerprint and
lookup calls and attaches a run to put and list operations.

```csharp
using HyperCache;

using var client = new Client();
var session = new Session(client);

await session.WithRunAsync("run-1", async s =>
{
    await s.FingerprintAsync("step one");
    await s.FingerprintAsync("step two"); // automatically chains from the previous fingerprint
    await s.CachePutAsync(fingerprint, data); // automatically tagged with run-1
});
```

## Pipeline

```csharp
using HyperCache;
using HyperCache.Workflows;

using var client = new Client();
using var pipeline = new Pipeline(client, run: "exp-1");

await pipeline.RecordAsync("embed", "document text");

CachedResult<string> result = await pipeline.CachedAsync(
    "summarize",
    "document text",
    computeFn: async () =>
    {
        // Only called on a cache miss.
        return await SummarizeAsync("document text");
    },
    ttl: 3600);

PipelineReport report = pipeline.End();
Console.WriteLine($"steps={report.NSteps} hits={report.NHits} misses={report.NMisses}");
Console.WriteLine(report.ExportAudit());
```

On a cache miss, `Pipeline.CachedAsync` stores the computed value with the TTL you
pass. When you omit `ttl`, it defaults to `86400` seconds (one day), matching the
TypeScript and Go SDK pipeline behavior.

## Error handling

The SDK exposes a typed exception hierarchy:

```csharp
using HyperCache;

try
{
    await client.FingerprintAsync("data");
}
catch (AuthException)        { /* 401 */ }
catch (QuotaException)       { /* 402 */ }
catch (RateLimitException)   { /* 429 */ }
catch (ClientException ex)   { /* other 4xx; ex.Status has the code */ }
catch (ServerException)      { /* 5xx, network, or timeout */ }
catch (HyperCacheException)  { /* base type for all of the above */ }
```

HTTP status mapping:

```text
401        -> AuthException
402        -> QuotaException
429        -> RateLimitException
Other 4xx  -> ClientException
5xx        -> ServerException
Network    -> ServerException
Timeout    -> ServerException
```

## Notes

- **Labels are plaintext metadata.** Do not store secrets, PHI, or other
  sensitive data in labels or run identifiers.
- `CacheGetAsync` treats HTTP `404` as a cache miss and returns `null` instead of
  throwing.
- Quota fields (`OpsUsed`, `OpsCap`, `OpsRemaining`) are fractional and exposed as
  `double?`; missing quota headers are `null`.

## Development

From the .NET SDK directory (`sdks/dotnet`):

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet pack src/HyperCache.Sdk/HyperCache.Sdk.csproj -c Release -o ./artifacts
```

Integration tests under `tests/HyperCache.Sdk.IntegrationTests` are skipped unless
`HYPERCACHE_KEY` is set, so `dotnet test` never calls the live API by default.

## Repository

```text
https://github.com/Hyper-Cache/hypercache-sdk
```

## License

MIT
