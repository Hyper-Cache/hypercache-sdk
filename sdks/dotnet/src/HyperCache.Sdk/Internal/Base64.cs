using System;

namespace HyperCache.Internal;

/// <summary>
/// Base64 encoding and decoding helpers backed by the BCL.
/// </summary>
internal static class Base64
{
    /// <summary>
    /// Encodes bytes as a base64 string.
    /// </summary>
    public static string Encode(ReadOnlySpan<byte> bytes)
    {
#if NET8_0_OR_GREATER
        return Convert.ToBase64String(bytes);
#else
        return Convert.ToBase64String(bytes.ToArray());
#endif
    }

    /// <summary>
    /// Decodes a base64 string into bytes.
    /// </summary>
    /// <exception cref="ArgumentNullException">The input is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The input is not valid base64.</exception>
    public static byte[] Decode(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return Convert.FromBase64String(value);
    }
}
