namespace HyperCache;

/// <summary>
/// Options used when listing cache entries with <see cref="Client.CacheListAsync"/>.
/// </summary>
public sealed class CacheListOptions
{
    /// <summary>
    /// Gets or sets the time-window bucket (for example, <c>today</c>, <c>yesterday</c>,
    /// <c>this-week</c>, or a date such as <c>2024-01-31</c>). Defaults to <c>today</c>.
    /// </summary>
    public string? Bucket { get; set; }

    /// <summary>
    /// Gets or sets the time-of-day part filter: <c>AM</c>, <c>PM</c>, or <c>ALL</c>.
    /// Defaults to <c>ALL</c>.
    /// </summary>
    public string? Part { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of entries per response. Defaults to 100.
    /// Must be positive when supplied.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Gets or sets an optional run identifier to filter entries by.
    /// </summary>
    public string? Run { get; set; }

    /// <summary>
    /// Gets or sets an optional label prefix to filter entries by.
    /// </summary>
    public string? LabelPrefix { get; set; }

    /// <summary>
    /// Gets or sets an optional pagination cursor from a previous
    /// <see cref="CacheListResponse.NextCursor"/>.
    /// </summary>
    public int? Cursor { get; set; }
}
