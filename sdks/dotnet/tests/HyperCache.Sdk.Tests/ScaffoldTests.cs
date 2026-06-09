using Xunit;

namespace HyperCache.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void AuthException_IsHyperCacheException()
    {
        var exception = new AuthException("Invalid API key.");

        Assert.IsType<AuthException>(exception);
        Assert.IsAssignableFrom<HyperCacheException>(exception);
        Assert.Equal(401, exception.Status);
        Assert.Equal("Invalid API key.", exception.Message);
    }

    [Fact]
    public void Client_ExposesDefaultBaseUrl()
    {
        using var client = new Client();

        Assert.Equal("https://api.hypercache.ai/", client.BaseUrl);
    }
}
