using System;

namespace HyperCache;

/// <summary>
/// A single input item for <see cref="Client.CacheLookupBatchAsync"/>.
/// </summary>
public sealed class CacheLookupBatchItem
{
    /// <summary>
    /// Gets or sets the raw bytes to look up.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; }

    /// <summary>
    /// Gets or sets the number of layers used by the fingerprint operation.
    /// </summary>
    public int? Layers { get; set; }

    /// <summary>
    /// Gets or sets the token count hint used by the fingerprint operation.
    /// </summary>
    public int? NTok { get; set; }

    /// <summary>
    /// Gets or sets the previous fingerprint bytes for chain-aware lookups.
    /// </summary>
    public ReadOnlyMemory<byte>? Prev { get; set; }

    /// <summary>
    /// Gets or sets the previous fingerprint as a hexadecimal string. When both
    /// <see cref="Prev"/> and <see cref="PrevHex"/> are supplied, <see cref="PrevHex"/> wins.
    /// </summary>
    public string? PrevHex { get; set; }
}
