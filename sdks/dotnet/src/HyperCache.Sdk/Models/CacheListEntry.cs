namespace HyperCache;

/// <summary>
/// A single cache entry returned by <see cref="Client.CacheListAsync"/>.
/// </summary>
public sealed class CacheListEntry
{
    /// <summary>
    /// Gets or sets the entry fingerprint as a hexadecimal string.
    /// </summary>
    public string FingerprintHex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stored value size in bytes, when reported.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the storage timestamp (Unix seconds), when reported.
    /// </summary>
    public long? StoredAt { get; set; }

    /// <summary>
    /// Gets or sets the expiration timestamp (Unix seconds), when reported.
    /// </summary>
    public long? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the plaintext label metadata.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the run identifier.
    /// </summary>
    public string? Run { get; set; }
}
