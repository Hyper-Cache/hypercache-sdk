using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class CacheListAsyncTests : EndpointTestBase
{
    private static HttpResponseMessage SuccessResponse()
    {
        var response = Json(
            "{\"bucket\":\"today\",\"part\":\"ALL\",\"total_count\":1,\"total_bytes\":123," +
            "\"runs\":[{\"run\":\"run-123\",\"count\":1,\"total_bytes\":123,\"entries\":[" +
            "{\"fingerprint_hex\":\"abc123\",\"size_bytes\":123,\"stored_at\":1712340000," +
            "\"expires_at\":1712349999,\"label\":\"some-label\",\"run\":\"run-123\"}]}]," +
            "\"next_cursor\":42}");
        WithQuota(response);
        return response;
    }

    [Fact]
    public async Task UsesGetAndListPath()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheListAsync();

        Assert.Equal(HttpMethod.Get, stub.LastRequest!.Method);
        Assert.Equal("/v1/cache/list", stub.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task IncludesDefaultBucketPartAndLimit()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheListAsync();

        string query = stub.LastRequest!.RequestUri!.Query;
        Assert.Contains("bucket=today", query, StringComparison.Ordinal);
        Assert.Contains("part=ALL", query, StringComparison.Ordinal);
        Assert.Contains("limit=100", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncludesSuppliedBucketPartAndLimit()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheListAsync(new CacheListOptions
        {
            Bucket = "2024-01-31",
            Part = "AM",
            Limit = 25,
        });

        string query = stub.LastRequest!.RequestUri!.Query;
        Assert.Contains("bucket=2024-01-31", query, StringComparison.Ordinal);
        Assert.Contains("part=AM", query, StringComparison.Ordinal);
        Assert.Contains("limit=25", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncludesOptionalRun()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheListAsync(new CacheListOptions { Run = "run-1" });

        Assert.Contains("run=run-1", stub.LastRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncludesOptionalLabelPrefix()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheListAsync(new CacheListOptions { LabelPrefix = "pre" });

        Assert.Contains("label_prefix=pre", stub.LastRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncludesOptionalCursor()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheListAsync(new CacheListOptions { Cursor = 7 });

        Assert.Contains("cursor=7", stub.LastRequest!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OmitsOptionalFiltersWhenNotSupplied()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheListAsync();

        string query = stub.LastRequest!.RequestUri!.Query;
        Assert.DoesNotContain("run=", query, StringComparison.Ordinal);
        Assert.DoesNotContain("label_prefix=", query, StringComparison.Ordinal);
        Assert.DoesNotContain("cursor=", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UrlEncodesQueryValues()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheListAsync(new CacheListOptions
        {
            Run = "a b&c",
            LabelPrefix = "x/y",
        });

        string query = stub.LastRequest!.RequestUri!.Query;
        Assert.Contains("run=a%20b%26c", query, StringComparison.Ordinal);
        Assert.Contains("label_prefix=x%2Fy", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeserializesGroupedRunResponse()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        CacheListResponse result = await client.CacheListAsync();

        Assert.Equal("today", result.Bucket);
        Assert.Equal("ALL", result.Part);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(123, result.TotalBytes);
        Assert.Single(result.Runs);
        Assert.Equal("run-123", result.Runs[0].Run);
        Assert.Equal(1, result.Runs[0].Count);
        Assert.Equal(123, result.Runs[0].TotalBytes);
        Assert.Single(result.Runs[0].Entries);
    }

    [Fact]
    public async Task ParsesNextCursor()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        CacheListResponse result = await client.CacheListAsync();

        Assert.Equal(42, result.NextCursor);
    }

    [Fact]
    public async Task ParsesEntryFields()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        CacheListResponse result = await client.CacheListAsync();
        CacheListEntry entry = result.Runs[0].Entries[0];

        Assert.Equal("abc123", entry.FingerprintHex);
        Assert.Equal(123, entry.SizeBytes);
        Assert.Equal(1712340000, entry.StoredAt);
        Assert.Equal(1712349999, entry.ExpiresAt);
        Assert.Equal("some-label", entry.Label);
        Assert.Equal("run-123", entry.Run);
    }

    [Fact]
    public async Task ParsesFractionalQuotaHeaders()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        CacheListResponse result = await client.CacheListAsync();

        Assert.Equal(1.25, result.OpsUsed);
        Assert.Equal(1000.5, result.OpsCap);
        Assert.Equal(998.25, result.OpsRemaining);
    }

    [Fact]
    public async Task InvalidLimitThrows()
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.CacheListAsync(new CacheListOptions { Limit = 0 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyBucketThrows(string bucket)
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CacheListAsync(new CacheListOptions { Bucket = bucket }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyPartThrows(string part)
    {
        var (client, _) = CreateClient((_, _) => SuccessResponse());

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CacheListAsync(new CacheListOptions { Part = part }));
    }

    [Fact]
    public async Task SendsAuthorizationAndUserAgentHeaders()
    {
        var (client, stub) = CreateClient((_, _) => SuccessResponse());

        await client.CacheListAsync();

        Assert.Equal("Bearer test-key", stub.LastRequest!.Headers.Authorization?.ToString());
        Assert.StartsWith(
            "hypercache-dotnet/",
            HeaderOrNull(stub.LastRequest, "User-Agent"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapsErrorStatusThroughPipeline()
    {
        var (client, _) = CreateClient((_, _) => Json("nope", System.Net.HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<AuthException>(() => client.CacheListAsync());
    }
}
