using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HyperCache.Internal;

namespace HyperCache;

/// <summary>
/// Client for the HyperCache API.
/// </summary>
public sealed class Client : IDisposable
{
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
    public Task<FingerprintResult> FingerprintAsync(
        ReadOnlyMemory<byte> data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        throw new NotImplementedException();
    }

    /// <summary>
    /// Looks up a value in HyperCache using the supplied bytes.
    /// </summary>
    public Task<CacheLookupResult> CacheLookupAsync(
        ReadOnlyMemory<byte> data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        throw new NotImplementedException();
    }

    /// <summary>
    /// Stores bytes in HyperCache under the supplied fingerprint.
    /// </summary>
    public Task<CachePutResult> CachePutAsync(
        string fingerprint,
        ReadOnlyMemory<byte> data,
        CachePutOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (fingerprint is null)
        {
            throw new ArgumentNullException(nameof(fingerprint));
        }

        throw new NotImplementedException();
    }

    /// <summary>
    /// Retrieves cached bytes by fingerprint. A cache miss returns null.
    /// </summary>
    public Task<byte[]?> CacheGetAsync(
        string fingerprint,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (fingerprint is null)
        {
            throw new ArgumentNullException(nameof(fingerprint));
        }

        throw new NotImplementedException();
    }

    /// <summary>
    /// Deletes a cached value by fingerprint. Delete is idempotent.
    /// </summary>
    public Task CacheDeleteAsync(
        string fingerprint,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (fingerprint is null)
        {
            throw new ArgumentNullException(nameof(fingerprint));
        }

        throw new NotImplementedException();
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
