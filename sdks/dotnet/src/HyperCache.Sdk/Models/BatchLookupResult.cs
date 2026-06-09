namespace HyperCache;

/// <summary>
/// The result of a single item in a <see cref="Client.CacheLookupBatchAsync"/> call.
/// </summary>
public sealed class BatchLookupResult
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
    /// Gets or sets the stored value size in bytes, when reported.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the storage timestamp, when reported.
    /// </summary>
    public long? StoredAt { get; set; }

    /// <summary>
    /// Gets or sets the expiration timestamp, when reported.
    /// </summary>
    public long? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the number of HyperCache ops used by the request.
    /// </summary>
    /// <remarks>
    /// Quota headers are reported per response, not per item. The same response-level
    /// quota values are applied to every item returned by a batch lookup.
    /// </remarks>
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
