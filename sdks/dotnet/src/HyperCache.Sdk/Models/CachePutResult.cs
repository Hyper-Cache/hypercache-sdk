namespace HyperCache;

/// <summary>
/// Represents the result of storing a value in HyperCache.
/// </summary>
public sealed class CachePutResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the value was stored.
    /// </summary>
    public bool Stored { get; set; }

    /// <summary>
    /// Gets or sets the stored value size in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the expiration timestamp, when available.
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
