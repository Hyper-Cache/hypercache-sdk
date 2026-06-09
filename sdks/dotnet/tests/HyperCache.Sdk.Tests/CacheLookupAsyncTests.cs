using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class CacheLookupAsyncTests : EndpointTestBase
{
    private static HttpResponseMessage HitResponse(byte[] value, string fingerprint)
    {
        var response = Bytes(value);
        response.Headers.TryAddWithoutValidation("X-Hc-Cache-Hit", "1");
        response.Headers.TryAddWithoutValidation("X-Hc-Fingerprint", fingerprint);
        WithQuota(response);
        return response;
    }

    private static HttpResponseMessage MissResponse(string fingerprint, bool expired)
    {
        var response = Json(
            "{\"fingerprint_hex\":\"" + fingerprint + "\",\"expired\":" + (expired ? "true" : "false") + "}");
        WithQuota(response);
        return response;
    }

    [Fact]
    public async Task UsesPostToLookupEndpoint()
    {
        var (client, stub) = CreateClient((_, _) => MissResponse("fp", false));

        await client.CacheLookupAsync(new byte[] { 1 });

        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal(BaseUrl + "/v1/cache/lookup", stub.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendsRawBodyAndHintHeaders()
    {
        byte[] payload = { 4, 5 };
        byte[]? captured = null;
        var (client, stub) = CreateClient((req, _) =>
        {
            captured = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return MissResponse("fp", false);
        });

        await client.CacheLookupAsync(new ReadOnlyMemory<byte>(payload));

        Assert.Equal(payload, captured);
        Assert.Equal("application/octet-stream", stub.LastRequest!.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("32", HeaderOrNull(stub.LastRequest, "X-Hc-Layers"));
        Assert.Equal("64", HeaderOrNull(stub.LastRequest, "X-Hc-N-Tok"));
    }

    [Fact]
    public async Task HitReturnsValueAndFingerprint()
    {
        byte[] value = { 100, 101, 102 };
        var (client, _) = CreateClient((_, _) => HitResponse(value, "deadbeef"));

        CacheLookupResult result = await client.CacheLookupAsync(new byte[] { 1 });

        Assert.True(result.Hit);
        Assert.False(result.Expired);
        Assert.Equal("deadbeef", result.FingerprintHex);
        Assert.Equal(value, result.Value);
    }

    [Fact]
    public async Task MissReturnsJsonFields()
    {
        var (client, _) = CreateClient((_, _) => MissResponse("cafe", expired: true));

        CacheLookupResult result = await client.CacheLookupAsync(new byte[] { 1 });

        Assert.False(result.Hit);
        Assert.Null(result.Value);
        Assert.Equal("cafe", result.FingerprintHex);
        Assert.True(result.Expired);
    }

    [Fact]
    public async Task ParsesQuotaHeadersOnHit()
    {
        var (client, _) = CreateClient((_, _) => HitResponse(new byte[] { 1 }, "fp"));

        CacheLookupResult result = await client.CacheLookupAsync(new byte[] { 1 });

        Assert.Equal(1.25, result.OpsUsed);
        Assert.Equal(1000.5, result.OpsCap);
        Assert.Equal(998.25, result.OpsRemaining);
    }

    [Fact]
    public async Task ParsesQuotaHeadersOnMiss()
    {
        var (client, _) = CreateClient((_, _) => MissResponse("fp", false));

        CacheLookupResult result = await client.CacheLookupAsync(new byte[] { 1 });

        Assert.Equal(1.25, result.OpsUsed);
        Assert.Equal(998.25, result.OpsRemaining);
    }

    [Fact]
    public async Task SendsPrevHeader()
    {
        var (client, stub) = CreateClient((_, _) => MissResponse("fp", false));

        await client.CacheLookupAsync(
            new byte[] { 1 },
            new FingerprintOptions { Prev = new ReadOnlyMemory<byte>(new byte[] { 0x0F, 0xAB }) });

        Assert.Equal("0fab", HeaderOrNull(stub.LastRequest!, "X-Hc-Prev"));
    }

    [Fact]
    public async Task NullStringOverloadThrows()
    {
        var (client, _) = CreateClient((_, _) => MissResponse("fp", false));

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CacheLookupAsync((string)null!));
    }
}
