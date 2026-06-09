namespace HyperCache;

/// <summary>
/// Represents the result of a HyperCache fingerprint operation.
/// </summary>
public sealed class FingerprintResult
{
    /// <summary>
    /// Gets or sets the raw fingerprint record.
    /// </summary>
    public byte[] Record { get; set; } = System.Array.Empty<byte>();

    /// <summary>
    /// Gets or sets the fingerprint as a hexadecimal string.
    /// </summary>
    public string RecordHex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fingerprint protocol version.
    /// </summary>
    public int Version { get; set; }

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
