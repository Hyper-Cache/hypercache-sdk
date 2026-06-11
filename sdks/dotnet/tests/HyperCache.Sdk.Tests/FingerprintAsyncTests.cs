using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class FingerprintAsyncTests : EndpointTestBase
{
    private const string FingerprintHex = "0fab";

    private static HttpResponseMessage SuccessResponse()
    {
        var response = Json("{\"fingerprint_hex\":\"" + FingerprintHex + "\",\"version\":3}");
        WithQuota(response);
        return response;
    }

    [Fact]
    public async Task UsesPostToFingerprintEndpoint()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.FingerprintAsync(new byte[] { 1, 2, 3 });

        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal(BaseUrl + "/v1/fingerprint", stub.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendsRawBodyAndOctetStreamContentType()
    {
        byte[] payload = { 9, 8, 7, 6 };
        byte[]? captured = null;
        var (client, stub) = CreateClient((req, _) =>
        {
            captured = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return SuccessResponse();
        });

        await client.FingerprintAsync(new ReadOnlyMemory<byte>(payload));

        Assert.Equal(payload, captured);
        Assert.Equal("application/octet-stream", stub.LastRequest!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task SendsDefaultHints()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.FingerprintAsync(new byte[] { 1 });

        Assert.Equal("32", HeaderOrNull(stub.LastRequest!, "X-Hc-Layers"));
        Assert.Equal("64", HeaderOrNull(stub.LastRequest, "X-Hc-N-Tok"));
    }

    [Fact]
    public async Task SendsCustomHints()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.FingerprintAsync(
            new byte[] { 1 },
            new FingerprintOptions { Layers = 12, NTok = 40 });

        Assert.Equal("12", HeaderOrNull(stub.LastRequest!, "X-Hc-Layers"));
        Assert.Equal("40", HeaderOrNull(stub.LastRequest, "X-Hc-N-Tok"));
    }

    [Fact]
    public async Task SendsPrevFromPrevHex()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.FingerprintAsync(
            new byte[] { 1 },
            new FingerprintOptions { PrevHex = "abcd" });

        Assert.Equal("abcd", HeaderOrNull(stub.LastRequest!, "X-Hc-Prev"));
    }

    [Fact]
    public async Task SendsPrevFromPrevBytesAsHex()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.FingerprintAsync(
            new byte[] { 1 },
            new FingerprintOptions { Prev = new ReadOnlyMemory<byte>(new byte[] { 0x0F, 0xAB }) });

        Assert.Equal("0fab", HeaderOrNull(stub.LastRequest!, "X-Hc-Prev"));
    }

    [Fact]
    public async Task PrevHexWinsOverPrevBytes()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.FingerprintAsync(
            new byte[] { 1 },
            new FingerprintOptions
            {
                PrevHex = "1234",
                Prev = new ReadOnlyMemory<byte>(new byte[] { 0xFF }),
            });

        Assert.Equal("1234", HeaderOrNull(stub.LastRequest!, "X-Hc-Prev"));
    }

    [Fact]
    public async Task ParsesResponseFields()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        FingerprintResult result = await client.FingerprintAsync(new byte[] { 1 });

        Assert.Equal(FingerprintHex, result.RecordHex);
        Assert.Equal(new byte[] { 0x0F, 0xAB }, result.Record);
        Assert.Equal(3, result.Version);
    }

    [Fact]
    public async Task ParsesFractionalQuotaHeaders()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        FingerprintResult result = await client.FingerprintAsync(new byte[] { 1 });

        Assert.Equal(1.25, result.OpsUsed);
        Assert.Equal(1000.5, result.OpsCap);
        Assert.Equal(998.25, result.OpsRemaining);
    }

    [Fact]
    public async Task StringOverloadUsesUtf8()
    {
        byte[]? captured = null;
        var (client, _) = CreateClient((req, _) =>
        {
            captured = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return SuccessResponse();
        });

        await client.FingerprintAsync("héllo");

        Assert.Equal(Encoding.UTF8.GetBytes("héllo"), captured);
    }

    [Fact]
    public async Task NullStringOverloadThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.FingerprintAsync((string)null!));
    }

    [Fact]
    public async Task NullByteArrayOverloadThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.FingerprintAsync((byte[])null!));
    }
}
