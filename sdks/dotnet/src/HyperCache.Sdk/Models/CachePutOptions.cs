namespace HyperCache;

/// <summary>
/// Options used when storing a value in HyperCache.
/// </summary>
public sealed class CachePutOptions
{
    /// <summary>
    /// Gets or sets the time-to-live in seconds.
    /// </summary>
    public int? Ttl { get; set; }

    /// <summary>
    /// Gets or sets the plaintext label metadata.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the run identifier.
    /// </summary>
    public string? Run { get; set; }
}
