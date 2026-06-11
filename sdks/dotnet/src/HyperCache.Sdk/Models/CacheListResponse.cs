using System;
using System.Collections.Generic;

namespace HyperCache;

/// <summary>
/// The result of a <see cref="Client.CacheListAsync"/> call. Entries are grouped
/// by run within the selected time window.
/// </summary>
public sealed class CacheListResponse
{
    /// <summary>
    /// Gets or sets the time-window bucket echoed by the server.
    /// </summary>
    public string? Bucket { get; set; }

    /// <summary>
    /// Gets or sets the time-of-day part echoed by the server.
    /// </summary>
    public string? Part { get; set; }

    /// <summary>
    /// Gets or sets the total number of matching entries across all groups.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the total size in bytes across all groups.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the run groups in this page of results.
    /// </summary>
    public IReadOnlyList<CacheListRunGroup> Runs { get; set; } = Array.Empty<CacheListRunGroup>();

    /// <summary>
    /// Gets or sets the cursor for the next page, or <see langword="null"/> when no
    /// further pages exist. Pass this value as <see cref="CacheListOptions.Cursor"/>.
    /// </summary>
    public int? NextCursor { get; set; }

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
