using System;
using System.Collections.Generic;
using System.Text;

namespace HyperCache.Internal;

/// <summary>
/// Builds URL query strings with percent-encoded names and values.
/// </summary>
/// <remarks>
/// Implemented with BCL primitives only so it works on both <c>netstandard2.0</c>
/// and <c>net8.0</c>; ASP.NET Core query helpers are intentionally not used.
/// Parameters with a <see langword="null"/> value are omitted.
/// </remarks>
internal static class QueryStringBuilder
{
    /// <summary>
    /// Builds a query string from the supplied parameters. Names and values are
    /// percent-encoded. Parameters whose value is <see langword="null"/> are omitted.
    /// </summary>
    /// <param name="parameters">The name/value pairs to encode.</param>
    /// <returns>
    /// A query string beginning with <c>?</c> when at least one parameter is present;
    /// otherwise an empty string.
    /// </returns>
    public static string Build(IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        var builder = new StringBuilder();

        foreach (KeyValuePair<string, string?> parameter in parameters)
        {
            if (parameter.Value is null)
            {
                continue;
            }

            builder.Append(builder.Length == 0 ? '?' : '&');
            builder.Append(Uri.EscapeDataString(parameter.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(parameter.Value));
        }

        return builder.ToString();
    }
}
