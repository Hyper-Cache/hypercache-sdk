using System;

namespace HyperCache.Internal;

/// <summary>
/// Hex encoding and decoding helpers.
/// </summary>
/// <remarks>
/// Output is lowercase to match the wire format produced by the other HyperCache
/// SDKs (Go's <c>hex.EncodeToString</c>, Rust, and Python all emit lowercase).
/// Decoding accepts either case. On <c>net8.0</c> the modern BCL helpers are used;
/// on <c>netstandard2.0</c> a manual implementation provides byte-exact behavior.
/// </remarks>
internal static class HexConvert
{
#if NET8_0_OR_GREATER
    /// <summary>
    /// Encodes bytes as a lowercase hexadecimal string.
    /// </summary>
    public static string ToHex(ReadOnlySpan<byte> bytes) =>
#pragma warning disable CA1308 // Normalize strings to uppercase: the wire format requires lowercase hex.
        Convert.ToHexString(bytes).ToLowerInvariant();
#pragma warning restore CA1308

    /// <summary>
    /// Decodes a hexadecimal string into bytes.
    /// </summary>
    /// <exception cref="FormatException">The input is not valid hexadecimal.</exception>
    public static byte[] FromHex(string hex)
    {
        if (hex is null)
        {
            throw new ArgumentNullException(nameof(hex));
        }

        return Convert.FromHexString(hex);
    }
#else
    private const string HexAlphabet = "0123456789abcdef";

    /// <summary>
    /// Encodes bytes as a lowercase hexadecimal string.
    /// </summary>
    public static string ToHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            chars[i * 2] = HexAlphabet[b >> 4];
            chars[(i * 2) + 1] = HexAlphabet[b & 0x0F];
        }

        return new string(chars);
    }

    /// <summary>
    /// Decodes a hexadecimal string into bytes.
    /// </summary>
    /// <exception cref="FormatException">The input is not valid hexadecimal.</exception>
    public static byte[] FromHex(string hex)
    {
        if (hex is null)
        {
            throw new ArgumentNullException(nameof(hex));
        }

        if ((hex.Length & 1) != 0)
        {
            throw new FormatException("Hexadecimal input must have an even number of characters.");
        }

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            int high = FromHexNibble(hex[i * 2]);
            int low = FromHexNibble(hex[(i * 2) + 1]);
            bytes[i] = (byte)((high << 4) | low);
        }

        return bytes;
    }

    private static int FromHexNibble(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        if (c >= 'a' && c <= 'f')
        {
            return c - 'a' + 10;
        }

        if (c >= 'A' && c <= 'F')
        {
            return c - 'A' + 10;
        }

        throw new FormatException($"Invalid hexadecimal character '{c}'.");
    }
#endif
}
