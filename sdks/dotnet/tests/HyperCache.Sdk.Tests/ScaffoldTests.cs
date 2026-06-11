using System;
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
        string? previousBaseUrl = Environment.GetEnvironmentVariable("HYPERCACHE_BASE_URL");
        try
        {
            // Ensure no ambient base-URL override interferes with the default assertion.
            Environment.SetEnvironmentVariable("HYPERCACHE_BASE_URL", null);

            // An API key is required, but BaseUrl is left at its default.
            using var client = new Client(new HyperCacheClientOptions { ApiKey = "k" });

            Assert.Equal("https://api.hypercache.ai/", client.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HYPERCACHE_BASE_URL", previousBaseUrl);
        }
    }
}
