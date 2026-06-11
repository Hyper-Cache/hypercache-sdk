namespace HyperCache.Workflows;

/// <summary>
/// Represents one recorded step in a <see cref="Pipeline"/>.
/// </summary>
public sealed class PipelineStep
{
    /// <summary>
    /// Gets or sets the step label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fingerprint produced for the step, as a hexadecimal string.
    /// </summary>
    public string FingerprintHex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the step was served from the cache.
    /// </summary>
    public bool WasHit { get; set; }

    /// <summary>
    /// Gets or sets the size of the step value in bytes, when known.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the number of HyperCache ops used by the step.
    /// </summary>
    public double? OpsUsed { get; set; }

    /// <summary>
    /// Gets or sets the HyperCache ops cap reported during the step.
    /// </summary>
    public double? OpsCap { get; set; }

    /// <summary>
    /// Gets or sets the remaining HyperCache ops reported during the step.
    /// </summary>
    public double? OpsRemaining { get; set; }

    /// <summary>
    /// Gets or sets the wall-clock time elapsed for the step, in seconds.
    /// </summary>
    public double ElapsedSeconds { get; set; }
}
