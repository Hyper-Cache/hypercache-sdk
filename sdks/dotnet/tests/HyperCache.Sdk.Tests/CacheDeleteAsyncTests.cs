using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class CacheDeleteAsyncTests : EndpointTestBase
{
    [Fact]
    public async Task UsesDeleteAndUrlEscapesFingerprint()
    {
        var (client, stub) = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.CacheDeleteAsync("ab cd");

        Assert.Equal(HttpMethod.Delete, stub.LastRequest!.Method);
        Assert.Equal(BaseUrl + "/v1/cache/ab%20cd", stub.LastRequest.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ReturnsNormallyOnSuccess()
    {
        var (client, _) = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        await client.CacheDeleteAsync("fp");
    }

    [Fact]
    public async Task ReturnsNormallyOn404()
    {
        var (client, _) = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        await client.CacheDeleteAsync("fp");
    }

    [Fact]
    public async Task ThrowsMappedExceptionOnNon404Error()
    {
        var (client, _) = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<ServerException>(() => client.CacheDeleteAsync("fp"));
    }

    [Fact]
    public async Task NullFingerprintThrows()
    {
        var (client, _) = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CacheDeleteAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceFingerprintThrows(string fingerprint)
    {
        var (client, _) = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<ArgumentException>(() => client.CacheDeleteAsync(fingerprint));
    }
}
