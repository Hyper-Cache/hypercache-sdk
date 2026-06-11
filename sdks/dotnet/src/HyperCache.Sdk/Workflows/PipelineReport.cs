using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HyperCache.Workflows;

/// <summary>
/// Summarizes a completed HyperCache <see cref="Pipeline"/>.
/// </summary>
public sealed class PipelineReport
{
    /// <summary>
    /// Gets or sets the total number of recorded steps.
    /// </summary>
    public int NSteps { get; set; }

    /// <summary>
    /// Gets or sets the number of steps served from the cache.
    /// </summary>
    public int NHits { get; set; }

    /// <summary>
    /// Gets or sets the number of steps that were computed (cache misses).
    /// </summary>
    public int NMisses { get; set; }

    /// <summary>
    /// Gets or sets the total wall-clock time across all steps, in seconds.
    /// </summary>
    public double TotalSeconds { get; set; }

    /// <summary>
    /// Gets or sets the final chain fingerprint (hex) of the pipeline, when available.
    /// </summary>
    public string? Chain { get; set; }

    /// <summary>
    /// Gets or sets the recorded steps in order.
    /// </summary>
    public IReadOnlyList<PipelineStep> Steps { get; set; } = Array.Empty<PipelineStep>();

    /// <summary>
    /// Produces a deterministic, human-readable audit of the pipeline suitable for logs or debugging.
    /// </summary>
    /// <returns>The formatted audit text.</returns>
    public string ExportAudit()
    {
        var builder = new StringBuilder();
        builder.Append("HyperCache Pipeline Report\n");
        builder.Append("Steps: ").Append(NSteps.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("Hits: ").Append(NHits.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("Misses: ").Append(NMisses.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("TotalSeconds: ")
            .Append(TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture))
            .Append('\n');
        builder.Append("Chain: ").Append(Chain ?? string.Empty).Append('\n');
        builder.Append('\n');

        foreach (PipelineStep step in Steps)
        {
            builder.Append("- label=").Append(step.Label)
                .Append(" fingerprint=").Append(step.FingerprintHex)
                .Append(" hit=").Append(step.WasHit ? "true" : "false")
                .Append(" elapsed=")
                .Append(step.ElapsedSeconds.ToString("0.000", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return builder.ToString();
    }
}
