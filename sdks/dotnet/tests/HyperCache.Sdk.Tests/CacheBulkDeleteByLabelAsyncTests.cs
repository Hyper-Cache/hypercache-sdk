using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class CacheBulkDeleteByLabelAsyncTests : EndpointTestBase
{
    private static HttpResponseMessage SuccessResponse()
    {
        var response = Json("{\"deleted\":10,\"bytes_freed\":2048}");
        WithQuota(response);
        return response;
    }

    [Fact]
    public async Task UsesDeleteAndByLabelPath()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheBulkDeleteByLabelAsync("pre", 10);

        Assert.Equal(HttpMethod.Delete, stub.LastRequest!.Method);
        Assert.Equal("/v1/cache/by-label", stub.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SendsLabelPrefixAndConfirm()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheBulkDeleteByLabelAsync("pre", 10);

        string query = stub.LastRequest!.RequestUri!.Query;
        Assert.Contains("label_prefix=pre", query, StringComparison.Ordinal);
        Assert.Contains("confirm=10", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UrlEncodesLabelPrefix()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheBulkDeleteByLabelAsync("a b&c", 3);

        Assert.Contains("label_prefix=a%20b%26c", stub.LastRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeserializesResponseFields()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        BulkDeleteResult result = await client.CacheBulkDeleteByLabelAsync("pre", 10);

        Assert.Equal(10, result.Deleted);
        Assert.Equal(2048, result.BytesFreed);
    }

    [Fact]
    public async Task ParsesFractionalQuotaHeaders()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        BulkDeleteResult result = await client.CacheBulkDeleteByLabelAsync("pre", 10);

        Assert.Equal(1.25, result.OpsUsed);
        Assert.Equal(1000.5, result.OpsCap);
        Assert.Equal(998.25, result.OpsRemaining);
    }

    [Fact]
    public async Task NullLabelPrefixThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.CacheBulkDeleteByLabelAsync(null!, 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyLabelPrefixThrows(string labelPrefix)
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CacheBulkDeleteByLabelAsync(labelPrefix, 1));
    }

    [Fact]
    public async Task NegativeConfirmCountThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.CacheBulkDeleteByLabelAsync("pre", -1));
    }

    [Fact]
    public async Task SendsAuthorizationAndUserAgentHeaders()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheBulkDeleteByLabelAsync("pre", 10);

        Assert.Equal("Bearer test-key", stub.LastRequest!.Headers.Authorization?.ToString());
        Assert.StartsWith(
            "hypercache-dotnet/",
            HeaderOrNull(stub.LastRequest, "User-Agent"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapsErrorStatusThroughPipeline()
    {
        // HttpStatusCode.TooManyRequests (429) is not defined on .NET Framework; cast explicitly.
        var (client, _) = CreateClient((_, _) => Json("slow", (System.Net.HttpStatusCode)429));

        await Assert.ThrowsAsync<RateLimitException>(
            () => client.CacheBulkDeleteByLabelAsync("pre", 10));
    }
}
