using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HyperCache.Internal;
using Xunit;

namespace HyperCache.Tests;

/// <summary>
/// Exercises the compatibility paths used by the <c>netstandard2.0</c> build of the library.
/// </summary>
/// <remarks>
/// This test project multi-targets <c>net8.0</c> and <c>net48</c>. The <c>net48</c> target
/// consumes the <c>netstandard2.0</c> library assembly, so when these tests run under
/// <c>net48</c> they cover the manual <see cref="HexConvert"/> implementation, the
/// <see cref="Base64"/> helper compatibility behavior (which materializes a
/// <see cref="ReadOnlySpan{T}"/> via <c>ToArray()</c>), and the
/// <see cref="ReadOnlyMemory{T}"/> / <c>System.Memory</c> assumptions used on that path.
/// The same tests also run under <c>net8.0</c> for parity.
/// </remarks>
public sealed class NetStandardCompatibilityTests
{
    [Fact]
    public void HexConvert_RoundTrips_AllByteValues()
    {
        // Drives the manual nibble encode/decode used on netstandard2.0.
        var original = new byte[256];
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)i;
        }

        string hex = HexConvert.ToHex(original);
        byte[] roundTripped = HexConvert.FromHex(hex);

        Assert.Equal(512, hex.Length);
        Assert.Equal("000102", hex.Substring(0, 6));
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void HexConvert_ToHex_FromReadOnlyMemorySlice_HonorsOffset()
    {
        // A sliced ReadOnlyMemory<byte> must encode only the slice, validating the
        // System.Memory span behavior the helpers rely on.
        var buffer = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        ReadOnlyMemory<byte> slice = new ReadOnlyMemory<byte>(buffer).Slice(1, 2);

        string hex = HexConvert.ToHex(slice.Span);

        Assert.Equal("adbe", hex);
    }

    [Fact]
    public void Base64_RoundTrips_FromReadOnlyMemorySlice()
    {
        // On netstandard2.0, Base64.Encode materializes the span via ToArray(); verify a
        // non-zero-offset slice encodes correctly and decodes back to the same bytes.
        var buffer = new byte[] { 1, 2, 3, 4, 5 };
        ReadOnlyMemory<byte> slice = new ReadOnlyMemory<byte>(buffer).Slice(2, 3);

        string encoded = Base64.Encode(slice.Span);
        byte[] decoded = Base64.Decode(encoded);

        Assert.Equal(Convert.ToBase64String(new byte[] { 3, 4, 5 }), encoded);
        Assert.Equal(new byte[] { 3, 4, 5 }, decoded);
    }

    [Fact]
    public async Task Client_FingerprintAsync_OverStubHandler_WorksOnCurrentTargetFramework()
    {
        // End-to-end smoke test through the pipeline on whatever framework is executing,
        // ensuring the netstandard2.0-targeted library assembly behaves under net48 too.
        var stub = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"fingerprint_hex\":\"0fab\",\"version\":3}",
                Encoding.UTF8,
                "application/json"),
        });

        using var httpClient = new HttpClient(stub) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        using var client = new Client(new HyperCacheClientOptions
        {
            ApiKey = "k",
            HttpClient = httpClient,
        });

        FingerprintResult result = await client.FingerprintAsync(Encoding.UTF8.GetBytes("hello"));

        Assert.Equal("0fab", result.RecordHex);
        Assert.Equal(new byte[] { 0x0F, 0xAB }, result.Record);
    }
}
