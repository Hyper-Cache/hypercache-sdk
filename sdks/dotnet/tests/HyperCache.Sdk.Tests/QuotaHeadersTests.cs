using System.Net;
using System.Net.Http;
using HyperCache.Internal;
using Xunit;

namespace HyperCache.Tests;

public sealed class QuotaHeadersTests
{
    private static HttpResponseMessage ResponseWith(params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    [Fact]
    public void From_MissingHeaders_ReturnsNulls()
    {
        using var response = ResponseWith();

        var quota = QuotaHeaders.From(response);

        Assert.Null(quota.OpsUsed);
        Assert.Null(quota.OpsCap);
        Assert.Null(quota.OpsRemaining);
    }

    [Fact]
    public void From_NullResponse_ReturnsNulls()
    {
        var quota = QuotaHeaders.From(null);

        Assert.Null(quota.OpsUsed);
        Assert.Null(quota.OpsCap);
        Assert.Null(quota.OpsRemaining);
    }

    [Fact]
    public void From_IntegerValues_Parse()
    {
        using var response = ResponseWith(
            (QuotaHeaders.UsedHeader, "10"),
            (QuotaHeaders.CapHeader, "1000"),
            (QuotaHeaders.RemainingHeader, "990"));

        var quota = QuotaHeaders.From(response);

        Assert.Equal(10d, quota.OpsUsed);
        Assert.Equal(1000d, quota.OpsCap);
        Assert.Equal(990d, quota.OpsRemaining);
    }

    [Fact]
    public void From_FractionalValues_Parse()
    {
        using var response = ResponseWith(
            (QuotaHeaders.UsedHeader, "1.25"),
            (QuotaHeaders.CapHeader, "1000.5"),
            (QuotaHeaders.RemainingHeader, "999.25"));

        var quota = QuotaHeaders.From(response);

        Assert.Equal(1.25d, quota.OpsUsed);
        Assert.Equal(1000.5d, quota.OpsCap);
        Assert.Equal(999.25d, quota.OpsRemaining);
    }

    [Fact]
    public void From_MalformedValues_ReturnNull()
    {
        using var response = ResponseWith(
            (QuotaHeaders.UsedHeader, "not-a-number"),
            (QuotaHeaders.CapHeader, ""),
            (QuotaHeaders.RemainingHeader, "1.2.3"));

        var quota = QuotaHeaders.From(response);

        Assert.Null(quota.OpsUsed);
        Assert.Null(quota.OpsCap);
        Assert.Null(quota.OpsRemaining);
    }
}
