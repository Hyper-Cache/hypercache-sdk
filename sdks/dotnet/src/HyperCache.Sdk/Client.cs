using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyperCache;

/// <summary>
/// Client for the HyperCache API.
/// </summary>
public sealed class Client : IDisposable
{
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
    }

    /// <summary>
    /// Gets the HyperCache API base URL.
    /// </summary>
    public string BaseUrl { get; }

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
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Client));
        }
    }
}
