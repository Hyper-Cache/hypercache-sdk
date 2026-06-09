using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class CacheRelabelAsyncTests : EndpointTestBase
{
    private static HttpResponseMessage SuccessResponse()
    {
        var response = Json(
            "{\"relabeled\":true,\"fingerprint_hex\":\"abc123\",\"label\":\"new-label\",\"run\":\"new-run\"}");
        WithQuota(response);
        return response;
    }

    private static HttpResponseMessage ClearedResponse()
    {
        var response = Json(
            "{\"relabeled\":true,\"fingerprint_hex\":\"abc123\",\"label\":null,\"run\":null}");
        WithQuota(response);
        return response;
    }

    [Fact]
    public async Task UsesPostAndUrlEscapesFingerprint()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheRelabelAsync("a b", new CacheRelabelOptions { Label = "x" });

        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal(BaseUrl + "/v1/cache/a%20b/relabel", stub.LastRequest.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SendsJsonContentType()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheRelabelAsync("fp", new CacheRelabelOptions { Label = "x" });

        Assert.Equal("application/json", stub.LastRequest!.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task SendsLabelAndRunInJson()
    {
        string? captured = null;
        var (client, _) = CreateClient((req, _) =>
        {
            captured = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return SuccessResponse();
        });

        await client.CacheRelabelAsync("fp", new CacheRelabelOptions { Label = "L", Run = "R" });

        using var doc = JsonDocument.Parse(captured!);
        Assert.Equal("L", doc.RootElement.GetProperty("label").GetString());
        Assert.Equal("R", doc.RootElement.GetProperty("run").GetString());
    }

    [Fact]
    public async Task SendsExplicitNullsWhenClearing()
    {
        string? captured = null;
        var (client, _) = CreateClient((req, _) =>
        {
            captured = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return ClearedResponse();
        });

        await client.CacheRelabelAsync("fp", new CacheRelabelOptions { Label = null, Run = null });

        using var doc = JsonDocument.Parse(captured!);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("label").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("run").ValueKind);
    }

    [Fact]
    public async Task DeserializesResponseFields()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        CacheRelabelResult result = await client.CacheRelabelAsync(
            "fp",
            new CacheRelabelOptions { Label = "new-label", Run = "new-run" });

        Assert.True(result.Relabeled);
        Assert.Equal("abc123", result.FingerprintHex);
        Assert.Equal("new-label", result.Label);
        Assert.Equal("new-run", result.Run);
    }

    [Fact]
    public async Task ParsesFractionalQuotaHeaders()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        CacheRelabelResult result = await client.CacheRelabelAsync(
            "fp",
            new CacheRelabelOptions { Label = "x" });

        Assert.Equal(1.25, result.OpsUsed);
        Assert.Equal(1000.5, result.OpsCap);
        Assert.Equal(998.25, result.OpsRemaining);
    }

    [Fact]
    public async Task NullFingerprintThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.CacheRelabelAsync(null!, new CacheRelabelOptions { Label = "x" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyFingerprintThrows(string fingerprint)
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CacheRelabelAsync(fingerprint, new CacheRelabelOptions { Label = "x" }));
    }

    [Fact]
    public async Task NullOptionsThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.CacheRelabelAsync("fp", null!));
    }

    [Fact]
    public async Task SendsAuthorizationAndUserAgentHeaders()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheRelabelAsync("fp", new CacheRelabelOptions { Label = "x" });

        Assert.Equal("Bearer test-key", stub.LastRequest!.Headers.Authorization?.ToString());
        Assert.StartsWith(
            "hypercache-dotnet/",
            HeaderOrNull(stub.LastRequest, "User-Agent"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapsErrorStatusThroughPipeline()
    {
        var (client, _) = CreateClient((_, _) => Json("over", System.Net.HttpStatusCode.PaymentRequired));

        await Assert.ThrowsAsync<QuotaException>(
            () => client.CacheRelabelAsync("fp", new CacheRelabelOptions { Label = "x" }));
    }
}
