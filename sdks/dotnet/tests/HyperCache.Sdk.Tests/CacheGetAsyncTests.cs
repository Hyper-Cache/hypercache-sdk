using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class CacheGetAsyncTests : EndpointTestBase
{
    [Fact]
    public async Task UsesGetAndUrlEscapesFingerprint()
    {
        var (client, stub) = CreateClient((_, _) => Bytes(new byte[] { 1, 2, 3 }));

        await client.CacheGetAsync("ab/cd ef");

        Assert.Equal(HttpMethod.Get, stub.LastRequest!.Method);
        Assert.Equal(BaseUrl + "/v1/cache/ab%2Fcd%20ef", stub.LastRequest.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ReturnsRawBytesOn200()
    {
        byte[] body = { 10, 20, 30 };
        var (client, _) = CreateClient((_, _) => Bytes(body));

        byte[]? result = await client.CacheGetAsync("fp");

        Assert.Equal(body, result);
    }

    [Fact]
    public async Task ReturnsNullOn404()
    {
        var (client, _) = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        byte[]? result = await client.CacheGetAsync("fp");

        Assert.Null(result);
    }

    [Fact]
    public async Task ThrowsMappedExceptionOnNon404Error()
    {
        var (client, _) = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<ServerException>(() => client.CacheGetAsync("fp"));
    }

    [Fact]
    public async Task ThrowsClientExceptionOn400()
    {
        var (client, _) = CreateClient((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<ClientException>(() => client.CacheGetAsync("fp"));
    }

    [Fact]
    public async Task NullFingerprintThrows()
    {
        var (client, _) = CreateClient((_, _) => Bytes(Array.Empty<byte>()));

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CacheGetAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceFingerprintThrows(string fingerprint)
    {
        var (client, _) = CreateClient((_, _) => Bytes(Array.Empty<byte>()));

        await Assert.ThrowsAsync<ArgumentException>(() => client.CacheGetAsync(fingerprint));
    }

    [Fact]
    public async Task DisposedClientThrows()
    {
        var (client, _) = CreateClient((_, _) => Bytes(Array.Empty<byte>()));
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CacheGetAsync("fp"));
    }
}
