using System;
using HyperCache.Internal;
using Xunit;

namespace HyperCache.Tests;

public sealed class HexConvertTests
{
    [Fact]
    public void ToHex_ProducesLowercaseHex()
    {
        byte[] bytes = { 0x00, 0x0F, 0xAB, 0xFF };

        string hex = HexConvert.ToHex(bytes);

        Assert.Equal("000fabff", hex);
    }

    [Fact]
    public void ToHex_EmptyInput_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, HexConvert.ToHex(Array.Empty<byte>()));
    }

    [Fact]
    public void FromHex_DecodesBytes()
    {
        byte[] bytes = HexConvert.FromHex("000fabff");

        Assert.Equal(new byte[] { 0x00, 0x0F, 0xAB, 0xFF }, bytes);
    }

    [Fact]
    public void FromHex_AcceptsUppercase()
    {
        byte[] bytes = HexConvert.FromHex("0FAB");

        Assert.Equal(new byte[] { 0x0F, 0xAB }, bytes);
    }

    [Fact]
    public void RoundTrip_PreservesBytes()
    {
        byte[] original = new byte[256];
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)i;
        }

        string hex = HexConvert.ToHex(original);
        byte[] roundTripped = HexConvert.FromHex(hex);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void FromHex_OddLength_Throws()
    {
        Assert.Throws<FormatException>(() => HexConvert.FromHex("abc"));
    }

    [Fact]
    public void FromHex_InvalidCharacter_Throws()
    {
        Assert.Throws<FormatException>(() => HexConvert.FromHex("zz"));
    }
}
