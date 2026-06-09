using System.Globalization;
using System.Linq;
using System.Net.Http;

namespace HyperCache.Internal;

/// <summary>
/// Parsed HyperCache quota headers (<c>X-Hc-Ops-*</c>).
/// </summary>
/// <remarks>
/// Quota values are fractional (for example, a cache hit can cost 1.25 ops), so
/// each value is parsed as <see cref="double"/> using the invariant culture.
/// Missing or malformed headers yield <see langword="null"/> rather than throwing.
/// </remarks>
internal readonly struct QuotaHeaders
{
    /// <summary>The <c>X-Hc-Ops-Used</c> header.</summary>
    public const string UsedHeader = "X-Hc-Ops-Used";

    /// <summary>The <c>X-Hc-Ops-Cap</c> header.</summary>
    public const string CapHeader = "X-Hc-Ops-Cap";

    /// <summary>The <c>X-Hc-Ops-Remaining</c> header.</summary>
    public const string RemainingHeader = "X-Hc-Ops-Remaining";

    private QuotaHeaders(double? opsUsed, double? opsCap, double? opsRemaining)
    {
        OpsUsed = opsUsed;
        OpsCap = opsCap;
        OpsRemaining = opsRemaining;
    }

    /// <summary>Gets the number of ops consumed by the request, if reported.</summary>
    public double? OpsUsed { get; }

    /// <summary>Gets the ops cap, if reported.</summary>
    public double? OpsCap { get; }

    /// <summary>Gets the remaining ops, if reported.</summary>
    public double? OpsRemaining { get; }

    /// <summary>
    /// Parses the quota headers from an HTTP response.
    /// </summary>
    /// <param name="response">The response to read headers from.</param>
    /// <returns>The parsed quota values; missing or malformed values are <see langword="null"/>.</returns>
    public static QuotaHeaders From(HttpResponseMessage? response)
    {
        if (response is null)
        {
            return default;
        }

        return new QuotaHeaders(
            ParseHeader(response, UsedHeader),
            ParseHeader(response, CapHeader),
            ParseHeader(response, RemainingHeader));
    }

    private static double? ParseHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        var raw = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return double.TryParse(
            raw,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : (double?)null;
    }
}
