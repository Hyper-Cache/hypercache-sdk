namespace HyperCache;

/// <summary>
/// The result of a bulk delete call (<see cref="Client.CacheBulkDeleteByLabelAsync"/>
/// or <see cref="Client.CacheBulkDeleteByAgeAsync"/>).
/// </summary>
public sealed class BulkDeleteResult
{
    /// <summary>
    /// Gets or sets the number of entries deleted.
    /// </summary>
    public long Deleted { get; set; }

    /// <summary>
    /// Gets or sets the total number of bytes freed.
    /// </summary>
    public long BytesFreed { get; set; }

    /// <summary>
    /// Gets or sets the cutoff timestamp (Unix seconds) used for an age-based delete,
    /// when reported. Not set for label-based deletes.
    /// </summary>
    public long? CutoffUnix { get; set; }

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
