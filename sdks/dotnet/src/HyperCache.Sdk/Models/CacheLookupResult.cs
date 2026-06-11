namespace HyperCache;

/// <summary>
/// Represents the result of a HyperCache cache lookup.
/// </summary>
public sealed class CacheLookupResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the lookup was a cache hit.
    /// </summary>
    public bool Hit { get; set; }

    /// <summary>
    /// Gets or sets the fingerprint associated with the lookup.
    /// </summary>
    public string FingerprintHex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cached value bytes when the lookup was a hit.
    /// </summary>
    public byte[]? Value { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cache entry was expired.
    /// </summary>
    public bool Expired { get; set; }

    /// <summary>
    /// Gets or sets the number of HyperCache ops used by the request.
    /// </summary>
    public double? OpsUsed { get; set; }

    /// <summary>
    /// Gets or sets the HyperCache ops cap.
    /// </summary>
    public double? OpsCap { get; set; }

    /// <summary>
    /// Gets or sets the remaining HyperCache ops.
    /// </summary>
    public double? OpsRemaining { get; set; }
}
