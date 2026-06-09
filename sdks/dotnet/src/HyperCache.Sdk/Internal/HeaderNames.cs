namespace HyperCache.Internal;

/// <summary>
/// HyperCache wire header names, centralized to avoid duplicated string literals.
/// </summary>
internal static class HeaderNames
{
    /// <summary>The fingerprint layer-count hint header.</summary>
    public const string Layers = "X-Hc-Layers";

    /// <summary>The fingerprint token-count hint header.</summary>
    public const string NTok = "X-Hc-N-Tok";

    /// <summary>The previous-fingerprint (hex) chaining header.</summary>
    public const string Prev = "X-Hc-Prev";

    /// <summary>The time-to-live (seconds) header used on put.</summary>
    public const string Ttl = "X-Hc-TTL";

    /// <summary>The label metadata header used on put.</summary>
    public const string Label = "X-Hc-Label";

    /// <summary>The run identifier header used on put.</summary>
    public const string Run = "X-Hc-Run";

    /// <summary>The cache-hit indicator header returned by lookup.</summary>
    public const string CacheHit = "X-Hc-Cache-Hit";

    /// <summary>The fingerprint (hex) header returned by lookup on a hit.</summary>
    public const string Fingerprint = "X-Hc-Fingerprint";
}
