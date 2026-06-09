using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyperCache;

/// <summary>
/// Static convenience methods for common HyperCache operations.
/// </summary>
public static class HyperCacheClient
{
    /// <summary>
    /// Generates a HyperCache fingerprint using a default client.
    /// </summary>
    public static Task<FingerprintResult> FingerprintAsync(
        ReadOnlyMemory<byte> data,
        FingerprintOptions? options = null,
        CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Retrieves cached bytes by fingerprint using a default client.
    /// </summary>
    public static Task<byte[]?> CacheGetAsync(
        string fingerprint,
        CancellationToken ct = default)
    {
        if (fingerprint is null)
        {
            throw new ArgumentNullException(nameof(fingerprint));
        }

        throw new NotImplementedException();
    }
}
