using System;
using System.Collections.Generic;

namespace HyperCache;

/// <summary>
/// A group of cache entries sharing the same run, returned by
/// <see cref="Client.CacheListAsync"/>.
/// </summary>
public sealed class CacheListRunGroup
{
    /// <summary>
    /// Gets or sets the run identifier for this group.
    /// </summary>
    public string? Run { get; set; }

    /// <summary>
    /// Gets or sets the number of entries in this group, as reported by the server.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the total size in bytes of the entries in this group.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the entries in this run group.
    /// </summary>
    public IReadOnlyList<CacheListEntry> Entries { get; set; } = Array.Empty<CacheListEntry>();
}
