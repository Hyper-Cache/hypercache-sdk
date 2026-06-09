namespace HyperCache;

/// <summary>
/// Options used when relabeling a cache entry with <see cref="Client.CacheRelabelAsync"/>.
/// </summary>
/// <remarks>
/// A <see langword="null"/> value clears the corresponding field on the server.
/// </remarks>
public sealed class CacheRelabelOptions
{
    /// <summary>
    /// Gets or sets the new plaintext label, or <see langword="null"/> to clear the label.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the new run identifier, or <see langword="null"/> to clear the run.
    /// </summary>
    public string? Run { get; set; }
}
