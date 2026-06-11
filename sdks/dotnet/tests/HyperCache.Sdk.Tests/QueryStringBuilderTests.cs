using System.Collections.Generic;
using HyperCache.Internal;
using Xunit;

namespace HyperCache.Tests;

public sealed class QueryStringBuilderTests
{
    [Fact]
    public void ReturnsEmptyStringWhenNoParameters()
    {
        string result = QueryStringBuilder.Build(new List<KeyValuePair<string, string?>>());

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void OmitsNullValues()
    {
        var parameters = new[]
        {
            new KeyValuePair<string, string?>("a", null),
            new KeyValuePair<string, string?>("b", "1"),
        };

        string result = QueryStringBuilder.Build(parameters);

        Assert.Equal("?b=1", result);
    }

    [Fact]
    public void PrefixesWithQuestionMarkAndJoinsWithAmpersand()
    {
        var parameters = new[]
        {
            new KeyValuePair<string, string?>("a", "1"),
            new KeyValuePair<string, string?>("b", "2"),
        };

        string result = QueryStringBuilder.Build(parameters);

        Assert.Equal("?a=1&b=2", result);
    }

    [Fact]
    public void EncodesNamesAndValues()
    {
        var parameters = new[]
        {
            new KeyValuePair<string, string?>("a b", "x&y/z"),
        };

        string result = QueryStringBuilder.Build(parameters);

        Assert.Equal("?a%20b=x%26y%2Fz", result);
    }

    [Fact]
    public void KeepsEmptyStringValues()
    {
        var parameters = new[]
        {
            new KeyValuePair<string, string?>("a", string.Empty),
        };

        string result = QueryStringBuilder.Build(parameters);

        Assert.Equal("?a=", result);
    }
}
