using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class CachePutAsyncTests : EndpointTestBase
{
    private static HttpResponseMessage SuccessResponse()
    {
        var response = Json(
            "{\"stored\":true,\"size_bytes\":123,\"expires_at\":1712345678,\"label\":\"L\",\"run\":\"R\"}");
        WithQuota(response);
        return response;
    }

    [Fact]
    public async Task UsesPutAndUrlEscapesFingerprint()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CachePutAsync("a b", new byte[] { 1 });

        Assert.Equal(HttpMethod.Put, stub.LastRequest!.Method);
        Assert.Equal(BaseUrl + "/v1/cache/a%20b", stub.LastRequest.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SendsRawBodyAndOctetStreamContentType()
    {
        byte[] payload = { 5, 6, 7 };
        byte[]? captured = null;
        var (client, stub) = CreateClient((req, _) =>
        {
            captured = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return SuccessResponse();
        });

        await client.CachePutAsync("fp", new ReadOnlyMemory<byte>(payload));

        Assert.Equal(payload, captured);
        Assert.Equal("application/octet-stream", stub.LastRequest!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task SendsOptionalHeaders()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CachePutAsync(
            "fp",
            new byte[] { 1 },
            new CachePutOptions { Ttl = 90, Label = "my-label", Run = "my-run" });

        Assert.Equal("90", HeaderOrNull(stub.LastRequest!, "X-Hc-TTL"));
        Assert.Equal("my-label", HeaderOrNull(stub.LastRequest, "X-Hc-Label"));
        Assert.Equal("my-run", HeaderOrNull(stub.LastRequest, "X-Hc-Run"));
    }

    [Fact]
    public async Task OmitsOptionalHeadersWhenNotSupplied()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CachePutAsync("fp", new byte[] { 1 });

        Assert.Null(HeaderOrNull(stub.LastRequest!, "X-Hc-TTL"));
        Assert.Null(HeaderOrNull(stub.LastRequest, "X-Hc-Label"));
        Assert.Null(HeaderOrNull(stub.LastRequest, "X-Hc-Run"));
    }

    [Fact]
    public async Task ParsesResponseFields()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        CachePutResult result = await client.CachePutAsync("fp", new byte[] { 1 });

        Assert.True(result.Stored);
        Assert.Equal(123, result.SizeBytes);
        Assert.Equal(1712345678, result.ExpiresAt);
        Assert.Equal("L", result.Label);
        Assert.Equal("R", result.Run);
    }

    [Fact]
    public async Task ParsesFractionalQuotaHeaders()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        CachePutResult result = await client.CachePutAsync("fp", new byte[] { 1 });

        Assert.Equal(1.25, result.OpsUsed);
        Assert.Equal(1000.5, result.OpsCap);
        Assert.Equal(998.25, result.OpsRemaining);
    }

    [Fact]
    public async Task NullFingerprintThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.CachePutAsync(null!, new byte[] { 1 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceFingerprintThrows(string fingerprint)
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CachePutAsync(fingerprint, new byte[] { 1 }));
    }

    [Fact]
    public async Task NullByteArrayDataThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.CachePutAsync("fp", (byte[])null!));
    }
}
