using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyperCache;

/// <summary>
/// Static convenience methods for common HyperCache operations backed by a lazily initialized default client.
/// </summary>
/// <remarks>
/// The default client is created once on first use via environment-based configuration and reused across
/// calls. It is intentionally not disposed between calls. For full control over configuration and lifetime,
/// construct a <see cref="Client"/> directly.
/// </remarks>
public static class HyperCacheClient
{
    private static readonly Lazy<Client> DefaultClient =
        new(static () => new Client(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Generates a HyperCache fingerprint using the default client.
    /// </summary>
    /// <param name="data">The bytes to fingerprint.</param>
    /// <param name="options">Optional fingerprint options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The fingerprint result.</returns>
    public static Task<FingerprintResult> FingerprintAsync(
        ReadOnlyMemory<byte> data,
        FingerprintOptions? options = null,
        CancellationToken ct = default) =>
        DefaultClient.Value.FingerprintAsync(data, options, ct);

    /// <summary>
    /// Generates a HyperCache fingerprint for the supplied bytes using the default client.
    /// </summary>
    /// <param name="data">The bytes to fingerprint.</param>
    /// <param name="options">Optional fingerprint options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The fingerprint result.</returns>
    public static Task<FingerprintResult> FingerprintAsync(
        byte[] data,
        FingerprintOptions? options = null,
        CancellationToken ct = default) =>
        DefaultClient.Value.FingerprintAsync(data, options, ct);

    /// <summary>
    /// Generates a HyperCache fingerprint for the supplied UTF-8 string using the default client.
    /// </summary>
    /// <param name="data">The string to fingerprint.</param>
    /// <param name="options">Optional fingerprint options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The fingerprint result.</returns>
    public static Task<FingerprintResult> FingerprintAsync(
        string data,
        FingerprintOptions? options = null,
        CancellationToken ct = default) =>
        DefaultClient.Value.FingerprintAsync(data, options, ct);

    /// <summary>
    /// Stores bytes under the supplied fingerprint using the default client.
    /// </summary>
    /// <param name="fingerprint">The hexadecimal fingerprint to store under.</param>
    /// <param name="data">The bytes to store.</param>
    /// <param name="options">Optional put options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The put result.</returns>
    public static Task<CachePutResult> CachePutAsync(
        string fingerprint,
        ReadOnlyMemory<byte> data,
        CachePutOptions? options = null,
        CancellationToken ct = default) =>
        DefaultClient.Value.CachePutAsync(fingerprint, data, options, ct);

    /// <summary>
    /// Stores bytes under the supplied fingerprint using the default client.
    /// </summary>
    /// <param name="fingerprint">The hexadecimal fingerprint to store under.</param>
    /// <param name="data">The bytes to store.</param>
    /// <param name="options">Optional put options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The put result.</returns>
    public static Task<CachePutResult> CachePutAsync(
        string fingerprint,
        byte[] data,
        CachePutOptions? options = null,
        CancellationToken ct = default) =>
        DefaultClient.Value.CachePutAsync(fingerprint, data, options, ct);

    /// <summary>
    /// Stores a UTF-8 string under the supplied fingerprint using the default client.
    /// </summary>
    /// <param name="fingerprint">The hexadecimal fingerprint to store under.</param>
    /// <param name="data">The string to store.</param>
    /// <param name="options">Optional put options.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The put result.</returns>
    public static Task<CachePutResult> CachePutAsync(
        string fingerprint,
        string data,
        CachePutOptions? options = null,
        CancellationToken ct = default) =>
        DefaultClient.Value.CachePutAsync(fingerprint, data, options, ct);

    /// <summary>
    /// Retrieves cached bytes by fingerprint using the default client. A cache miss returns <see langword="null"/>.
    /// </summary>
    /// <param name="fingerprint">The hexadecimal fingerprint to retrieve.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>The cached bytes, or <see langword="null"/> on a miss.</returns>
    public static Task<byte[]?> CacheGetAsync(
        string fingerprint,
        CancellationToken ct = default) =>
        DefaultClient.Value.CacheGetAsync(fingerprint, ct);

    /// <summary>
    /// Deletes a cached value by fingerprint using the default client. Delete is idempotent.
    /// </summary>
    /// <param name="fingerprint">The hexadecimal fingerprint to delete.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A task that completes when the delete completes.</returns>
    public static Task CacheDeleteAsync(
        string fingerprint,
        CancellationToken ct = default) =>
        DefaultClient.Value.CacheDeleteAsync(fingerprint, ct);
}
