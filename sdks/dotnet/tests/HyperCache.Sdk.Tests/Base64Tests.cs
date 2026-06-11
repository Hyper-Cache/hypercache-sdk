using System;
using HyperCache.Internal;
using Xunit;

namespace HyperCache.Tests;

public sealed class Base64Tests
{
    [Fact]
    public void Encode_ProducesBase64()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("hello");

        string encoded = Base64.Encode(bytes);

        Assert.Equal("aGVsbG8=", encoded);
    }

    [Fact]
    public void Decode_ProducesBytes()
    {
        byte[] bytes = Base64.Decode("aGVsbG8=");

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("hello"), bytes);
    }

    [Fact]
    public void RoundTrip_PreservesBytes()
    {
        byte[] original = new byte[256];
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(255 - i);
        }

        byte[] roundTripped = Base64.Decode(Base64.Encode(original));

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void Decode_InvalidInput_Throws()
    {
        Assert.Throws<FormatException>(() => Base64.Decode("not valid base64!!!"));
    }

    [Fact]
    public void Decode_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Base64.Decode(null!));
    }
}
