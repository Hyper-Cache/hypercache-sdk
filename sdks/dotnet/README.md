# HyperCache.Sdk

An idiomatic C#/.NET SDK for the HyperCache API.

This package provides a .NET client for generating fingerprints, reading and writing cache values, and working with HyperCache workflows.

> Status: early scaffold. The public package structure is being established first; full endpoint implementation will be added in later phases.

## Installation

```bash
dotnet add package HyperCache.Sdk
```

## Target frameworks

This package targets:

- `netstandard2.0`
- `net8.0`

The `netstandard2.0` target provides broad compatibility with older .NET consumers, including .NET Framework-era applications. The `net8.0` target enables modern .NET runtime behavior and optimizations.

## Basic usage

```csharp
using System.Text;
using HyperCache;

using var client = new Client();

byte[] data = Encoding.UTF8.GetBytes("hello world");

FingerprintResult result = await client.FingerprintAsync(data);

Console.WriteLine(result.RecordHex);
```

## Configuration

By default, the SDK uses:

```text
https://api.hypercache.ai
```

The SDK is expected to support configuration through `HyperCacheClientOptions`:

```csharp
using HyperCache;

using var client = new Client(new HyperCacheClientOptions
{
    ApiKey = "your-api-key",
    BaseUrl = new Uri("https://api.hypercache.ai"),
    Timeout = TimeSpan.FromSeconds(30)
});
```

The SDK is also expected to support the following environment variables:

```text
HYPERCACHE_KEY
HYPERCACHE_BASE_URL
```

## Planned API shape

The primary SDK surface is async-only and Task-based.

```csharp
Task<FingerprintResult> FingerprintAsync(
    ReadOnlyMemory<byte> data,
    FingerprintOptions? options = null,
    CancellationToken ct = default);

Task<CacheLookupResult> CacheLookupAsync(
    ReadOnlyMemory<byte> data,
    FingerprintOptions? options = null,
    CancellationToken ct = default);

Task<CachePutResult> CachePutAsync(
    string fingerprint,
    ReadOnlyMemory<byte> data,
    CachePutOptions? options = null,
    CancellationToken ct = default);

Task<byte[]?> CacheGetAsync(
    string fingerprint,
    CancellationToken ct = default);

Task CacheDeleteAsync(
    string fingerprint,
    CancellationToken ct = default);
```

## Error handling

The SDK exposes a typed exception hierarchy:

```csharp
HyperCacheException
AuthException
QuotaException
RateLimitException
ClientException
ServerException
```

Expected HTTP status mapping:

```text
401        -> AuthException
402        -> QuotaException
429        -> RateLimitException
Other 4xx  -> ClientException
5xx        -> ServerException
Network    -> ServerException
Timeout    -> ServerException
```

Cache reads are expected to treat HTTP `404` as a cache miss and return `null`, not throw.

## Development

From the .NET SDK directory:

```bash
cd sdks/dotnet
```

Restore packages:

```bash
dotnet restore
```

Build:

```bash
dotnet build -c Release
```

Run tests:

```bash
dotnet test -c Release
```

Pack locally:

```bash
dotnet pack src/HyperCache.Sdk/HyperCache.Sdk.csproj -c Release -o ./artifacts
```

Expected package outputs:

```text
artifacts/HyperCache.Sdk.0.1.0.nupkg
artifacts/HyperCache.Sdk.0.1.0.snupkg
```

## Repository

Source repository:

```text
https://github.com/Hyper-Cache/hypercache-sdk
```

## License

MIT