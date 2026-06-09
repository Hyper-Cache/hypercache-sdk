using System;

namespace HyperCache;

/// <summary>
/// Options used when generating a HyperCache fingerprint.
/// </summary>
public sealed class FingerprintOptions
{
    /// <summary>
    /// Gets or sets the number of layers used by the fingerprint operation.
    /// </summary>
    public int? Layers { get; set; }

    /// <summary>
    /// Gets or sets the token count hint used by the fingerprint operation.
    /// </summary>
    public int? NTok { get; set; }

    /// <summary>
    /// Gets or sets the previous fingerprint bytes for chain-aware operations.
    /// </summary>
    public ReadOnlyMemory<byte>? Prev { get; set; }

    /// <summary>
    /// Gets or sets the previous fingerprint as a hexadecimal string.
    /// </summary>
    public string? PrevHex { get; set; }
}
