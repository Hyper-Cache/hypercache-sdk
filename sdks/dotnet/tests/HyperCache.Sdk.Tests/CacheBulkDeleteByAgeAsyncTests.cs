using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class CacheBulkDeleteByAgeAsyncTests : EndpointTestBase
{
    private static HttpResponseMessage SuccessResponse()
    {
        var response = Json("{\"deleted\":10,\"bytes_freed\":2048,\"cutoff_unix\":1712340000}");
        WithQuota(response);
        return response;
    }

    [Fact]
    public async Task UsesDeleteAndByAgePath()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheBulkDeleteByAgeAsync("30d", 10);

        Assert.Equal(HttpMethod.Delete, stub.LastRequest!.Method);
        Assert.Equal("/v1/cache/by-age", stub.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SendsOlderThanAndConfirm()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheBulkDeleteByAgeAsync("30d", 10);

        string query = stub.LastRequest!.RequestUri!.Query;
        Assert.Contains("older_than=30d", query, StringComparison.Ordinal);
        Assert.Contains("confirm=10", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UrlEncodesOlderThan()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheBulkDeleteByAgeAsync("1 w&x", 3);

        Assert.Contains("older_than=1%20w%26x", stub.LastRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeserializesResponseFields()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        BulkDeleteResult result = await client.CacheBulkDeleteByAgeAsync("30d", 10);

        Assert.Equal(10, result.Deleted);
        Assert.Equal(2048, result.BytesFreed);
        Assert.Equal(1712340000, result.CutoffUnix);
    }

    [Fact]
    public async Task ParsesFractionalQuotaHeaders()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        BulkDeleteResult result = await client.CacheBulkDeleteByAgeAsync("30d", 10);

        Assert.Equal(1.25, result.OpsUsed);
        Assert.Equal(1000.5, result.OpsCap);
        Assert.Equal(998.25, result.OpsRemaining);
    }

    [Fact]
    public async Task NullOlderThanThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.CacheBulkDeleteByAgeAsync(null!, 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOlderThanThrows(string olderThan)
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CacheBulkDeleteByAgeAsync(olderThan, 1));
    }

    [Fact]
    public async Task NegativeConfirmCountThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.CacheBulkDeleteByAgeAsync("30d", -1));
    }

    [Fact]
    public async Task SendsAuthorizationAndUserAgentHeaders()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheBulkDeleteByAgeAsync("30d", 10);

        Assert.Equal("Bearer test-key", stub.LastRequest!.Headers.Authorization?.ToString());
        Assert.StartsWith(
            "hypercache-dotnet/",
            HeaderOrNull(stub.LastRequest, "User-Agent"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapsErrorStatusThroughPipeline()
    {
        var (client, _) = CreateClient((_, _) => Json("boom", System.Net.HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<ServerException>(
            () => client.CacheBulkDeleteByAgeAsync("30d", 10));
    }
}
