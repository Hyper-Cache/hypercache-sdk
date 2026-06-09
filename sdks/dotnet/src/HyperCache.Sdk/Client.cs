using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HyperCache.Internal;

namespace HyperCache;

/// <summary>
/// Client for the HyperCache API.
/// </summary>
public sealed class Client : IDisposable
{
    /// <summary>The default layer-count fingerprint hint.</summary>
    internal const int DefaultLayers = 32;

    /// <summary>The default token-count fingerprint hint.</summary>
    internal const int DefaultNTok = 64;

    private const string OctetStream = "application/octet-stream";
    private const string JsonMediaType = "application/json";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly HttpPipeline _pipeline;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Client"/> class using environment-based configuration.
    /// </summary>
    public Client()
        : this(new HyperCacheClientOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Client"/> class.
    /// </summary>
    public Client(HyperCacheClientOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        BaseUrl = options.BaseUrl.ToString();

        if (options.HttpClient is not null)
        {
            // Injected client: use as-is and never dispose it (Go's WithHTTPClient parity).
            _httpClient = options.HttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                // Timeout is enforced per-request by the pipeline via a linked CTS so
                // that timeouts and caller cancellation remain distinguishable.
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
            _ownsHttpClient = true;
        }

        _pipeline = new HttpPipeline(_httpClient, options, ResolvePackageVersion());
    }

    /// <summary>
    /// Gets the HyperCache API base URL.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Gets the internal HTTP pipeline used by endpoint implementations.
    /// </summary>
    internal HttpPipeline Pipeline
    {
        get
        {
            ThrowIfDisposed();
            return _pipeline;
        }
    }

    /// <summary>
    /// Generates a HyperCache fingerprint for the supplied bytes.
    /// </summary>
    public async Task<FingerprintResult> FingerprintAsync(
        ReadOnlyMemory<byte> data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        using var request = new HttpRequestMessage(HttpMethod.Post, _pipeline.BuildUri("v1/fingerprint"))
        {
            Content = CreateOctetStreamContent(data),
        };
        ApplyFingerprintHeaders(request, options);

        (FingerprintResponse body, QuotaHeaders quota) = await _pipeline
            .SendForJsonWithQuotaAsync<FingerprintResponse>(request, ct)
            .ConfigureAwait(false);

        string recordHex = body.FingerprintHex ?? string.Empty;

        return new FingerprintResult
        {
            RecordHex = recordHex,
            Record = recordHex.Length == 0 ? Array.Empty<byte>() : HexConvert.FromHex(recordHex),
            Version = body.Version,
            OpsUsed = quota.OpsUsed,
            OpsCap = quota.OpsCap,
            OpsRemaining = quota.OpsRemaining,
        };
    }

    /// <summary>
    /// Generates a HyperCache fingerprint for the supplied bytes.
    /// </summary>
    public Task<FingerprintResult> FingerprintAsync(
        byte[] data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return FingerprintAsync(new ReadOnlyMemory<byte>(data), options, ct);
    }

    /// <summary>
    /// Generates a HyperCache fingerprint for the supplied UTF-8 string.
    /// </summary>
    public Task<FingerprintResult> FingerprintAsync(
        string data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return FingerprintAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(data)), options, ct);
    }

    /// <summary>
    /// Looks up a value in HyperCache using the supplied bytes.
    /// </summary>
    public async Task<CacheLookupResult> CacheLookupAsync(
        ReadOnlyMemory<byte> data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        using var request = new HttpRequestMessage(HttpMethod.Post, _pipeline.BuildUri("v1/cache/lookup"))
        {
            Content = CreateOctetStreamContent(data),
        };
        ApplyFingerprintHeaders(request, options);

        using HttpResponseMessage response = await _pipeline.SendAsync(request, ct).ConfigureAwait(false);

        QuotaHeaders quota = QuotaHeaders.From(response);
        var result = new CacheLookupResult
        {
            OpsUsed = quota.OpsUsed,
            OpsCap = quota.OpsCap,
            OpsRemaining = quota.OpsRemaining,
        };

        if (IsCacheHit(response))
        {
            result.Hit = true;
            result.Expired = false;
            result.FingerprintHex = GetHeaderValue(response, HeaderNames.Fingerprint) ?? string.Empty;
            result.Value = await HttpPipeline.ReadBytesAsync(response, ct).ConfigureAwait(false);
            return result;
        }

        byte[] payload = await HttpPipeline.ReadBytesAsync(response, ct).ConfigureAwait(false);
        var miss = HttpPipeline.Deserialize<CacheLookupMissResponse>(payload);

        result.Hit = false;
        result.Value = null;
        result.FingerprintHex = miss.FingerprintHex ?? string.Empty;
        result.Expired = miss.Expired;
        return result;
    }

    /// <summary>
    /// Looks up a value in HyperCache using the supplied bytes.
    /// </summary>
    public Task<CacheLookupResult> CacheLookupAsync(
        byte[] data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return CacheLookupAsync(new ReadOnlyMemory<byte>(data), options, ct);
    }

    /// <summary>
    /// Looks up a value in HyperCache using the supplied UTF-8 string.
    /// </summary>
    public Task<CacheLookupResult> CacheLookupAsync(
        string data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return CacheLookupAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(data)), options, ct);
    }

    /// <summary>
    /// Looks up multiple values in a single batch request, preserving input order.
    /// </summary>
    public async Task<IReadOnlyList<BatchLookupResult>> CacheLookupBatchAsync(
        IEnumerable<CacheLookupBatchItem> inputs,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (inputs is null)
        {
            throw new ArgumentNullException(nameof(inputs));
        }

        var items = new List<CacheLookupBatchItem>(inputs);
        var payload = new BatchLookupRequest();

        foreach (CacheLookupBatchItem item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("Batch lookup items must not be null.", nameof(inputs));
            }

            payload.Items.Add(new BatchLookupRequestItem
            {
                DataB64 = Base64.Encode(item.Data.Span),
                Layers = item.Layers,
                NTok = item.NTok,
                PrevHex = ResolvePrevHex(item.PrevHex, item.Prev),
            });
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _pipeline.BuildUri("v1/cache/lookup/batch"))
        {
            Content = CreateJsonContent(HttpPipeline.Serialize(payload)),
        };

        (BatchLookupResponse body, QuotaHeaders quota) = await _pipeline
            .SendForJsonWithQuotaAsync<BatchLookupResponse>(request, ct)
            .ConfigureAwait(false);

        var results = new List<BatchLookupResult>(body.Items.Count);
        foreach (BatchLookupResponseItem responseItem in body.Items)
        {
            results.Add(new BatchLookupResult
            {
                Hit = responseItem.Hit,
                FingerprintHex = responseItem.FingerprintHex ?? string.Empty,
                Value = responseItem.ValueB64 is null ? null : Base64.Decode(responseItem.ValueB64),
                Expired = responseItem.Expired,
                SizeBytes = responseItem.SizeBytes,
                StoredAt = responseItem.StoredAt,
                ExpiresAt = responseItem.ExpiresAt,
                OpsUsed = quota.OpsUsed,
                OpsCap = quota.OpsCap,
                OpsRemaining = quota.OpsRemaining,
            });
        }

        return results;
    }

    /// <summary>
    /// Stores bytes in HyperCache under the supplied fingerprint.
    /// </summary>
    public async Task<CachePutResult> CachePutAsync(
        string fingerprint,
        ReadOnlyMemory<byte> data,
        CachePutOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateFingerprint(fingerprint);

        using var request = new HttpRequestMessage(HttpMethod.Put, BuildCacheUri(fingerprint))
        {
            Content = CreateOctetStreamContent(data),
        };

        if (options is not null)
        {
            if (options.Ttl.HasValue)
            {
                request.Headers.TryAddWithoutValidation(
                    HeaderNames.Ttl,
                    options.Ttl.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (options.Label is not null)
            {
                request.Headers.TryAddWithoutValidation(HeaderNames.Label, options.Label);
            }

            if (options.Run is not null)
            {
                request.Headers.TryAddWithoutValidation(HeaderNames.Run, options.Run);
            }
        }

        (CachePutResponse body, QuotaHeaders quota) = await _pipeline
            .SendForJsonWithQuotaAsync<CachePutResponse>(request, ct)
            .ConfigureAwait(false);

        return new CachePutResult
        {
            Stored = body.Stored,
            SizeBytes = body.SizeBytes,
            ExpiresAt = body.ExpiresAt,
            Label = body.Label,
            Run = body.Run,
            OpsUsed = quota.OpsUsed,
            OpsCap = quota.OpsCap,
            OpsRemaining = quota.OpsRemaining,
        };
    }

    /// <summary>
    /// Stores bytes in HyperCache under the supplied fingerprint.
    /// </summary>
    public Task<CachePutResult> CachePutAsync(
        string fingerprint,
        byte[] data,
        CachePutOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return CachePutAsync(fingerprint, new ReadOnlyMemory<byte>(data), options, ct);
    }

    /// <summary>
    /// Stores a UTF-8 string in HyperCache under the supplied fingerprint.
    /// </summary>
    public Task<CachePutResult> CachePutAsync(
        string fingerprint,
        string data,
        CachePutOptions? options = null,
        CancellationToken ct = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return CachePutAsync(fingerprint, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(data)), options, ct);
    }

    /// <summary>
    /// Retrieves cached bytes by fingerprint. A cache miss returns null.
    /// </summary>
    public async Task<byte[]?> CacheGetAsync(
        string fingerprint,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateFingerprint(fingerprint);

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildCacheUri(fingerprint));

        using HttpResponseMessage response = await _pipeline
            .SendAllowingStatusAsync(request, 404, ct)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return await HttpPipeline.ReadBytesAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a cached value by fingerprint. Delete is idempotent.
    /// </summary>
    public async Task CacheDeleteAsync(
        string fingerprint,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateFingerprint(fingerprint);

        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildCacheUri(fingerprint));

        // A missing value is not an error: delete is idempotent.
        using HttpResponseMessage response = await _pipeline
            .SendAllowingStatusAsync(request, 404, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes resources owned by this client.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static void ValidateFingerprint(string fingerprint)
    {
        if (fingerprint is null)
        {
            throw new ArgumentNullException(nameof(fingerprint));
        }

        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new ArgumentException("Fingerprint must not be empty or whitespace.", nameof(fingerprint));
        }
    }

    private static ByteArrayContent CreateOctetStreamContent(ReadOnlyMemory<byte> data)
    {
        var content = new ByteArrayContent(ToArray(data));
        content.Headers.ContentType = new MediaTypeHeaderValue(OctetStream);
        return content;
    }

    private static ByteArrayContent CreateJsonContent(byte[] payload)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue(JsonMediaType);
        return content;
    }

    private static byte[] ToArray(ReadOnlyMemory<byte> data) =>
        System.Runtime.InteropServices.MemoryMarshal.TryGetArray(data, out var segment)
            && segment.Offset == 0
            && segment.Array is not null
            && segment.Count == segment.Array.Length
            ? segment.Array
            : data.ToArray();

    private static string? ResolvePrevHex(string? prevHex, ReadOnlyMemory<byte>? prev)
    {
        // PrevHex wins over Prev when both are supplied (single-lookup parity).
        if (!string.IsNullOrEmpty(prevHex))
        {
            return prevHex;
        }

        return prev.HasValue ? HexConvert.ToHex(prev.Value.Span) : null;
    }

    private static void ApplyFingerprintHeaders(HttpRequestMessage request, FingerprintOptions? options)
    {
        int layers = options?.Layers ?? DefaultLayers;
        int nTok = options?.NTok ?? DefaultNTok;

        request.Headers.TryAddWithoutValidation(
            HeaderNames.Layers,
            layers.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            HeaderNames.NTok,
            nTok.ToString(CultureInfo.InvariantCulture));

        string? prevHex = ResolvePrevHex(options?.PrevHex, options?.Prev);
        if (!string.IsNullOrEmpty(prevHex))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Prev, prevHex);
        }
    }

    private static bool IsCacheHit(HttpResponseMessage response) =>
        string.Equals(GetHeaderValue(response, HeaderNames.CacheHit), "1", StringComparison.Ordinal);

    private static string? GetHeaderValue(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            foreach (string value in values)
            {
                return value;
            }
        }

        return null;
    }

    private Uri BuildCacheUri(string fingerprint) =>
        _pipeline.BuildUri("v1/cache/" + Uri.EscapeDataString(fingerprint));

    private static string ResolvePackageVersion()
    {
        var assembly = typeof(Client).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip any source-control metadata suffix (e.g. "0.1.0+abc1234").
            int plus = informational!.IndexOf('+');
            return plus >= 0 ? informational.Substring(0, plus) : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Client));
        }
    }
}
