namespace HyperCache;

/// <summary>
/// The result of a <see cref="Client.CacheRelabelAsync"/> call.
/// </summary>
public sealed class CacheRelabelResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the entry was relabeled.
    /// </summary>
    public bool Relabeled { get; set; }

    /// <summary>
    /// Gets or sets the fingerprint of the relabeled entry as a hexadecimal string.
    /// </summary>
    public string FingerprintHex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resulting label, or <see langword="null"/> when cleared.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the resulting run identifier, or <see langword="null"/> when cleared.
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
