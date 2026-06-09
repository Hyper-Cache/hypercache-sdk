using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HyperCache.Workflows;
using Xunit;

namespace HyperCache.Tests;

public sealed class PipelineTests : EndpointTestBase
{
    private static HttpResponseMessage FingerprintResponse(string fingerprintHex)
    {
        var response = Json("{\"fingerprint_hex\":\"" + fingerprintHex + "\",\"version\":3}");
        WithQuota(response);
        return response;
    }

    private static HttpResponseMessage LookupHit(byte[] value, string fingerprint)
    {
        var response = Bytes(value);
        response.Headers.TryAddWithoutValidation("X-Hc-Cache-Hit", "1");
        response.Headers.TryAddWithoutValidation("X-Hc-Fingerprint", fingerprint);
        WithQuota(response);
        return response;
    }

    private static HttpResponseMessage LookupMiss(string fingerprint)
    {
        var response = Json("{\"fingerprint_hex\":\"" + fingerprint + "\",\"expired\":false}");
        WithQuota(response);
        return response;
    }

    private static HttpResponseMessage PutResponse()
    {
        var response = Json("{\"stored\":true,\"size_bytes\":5}");
        WithQuota(response);
        return response;
    }

    [Fact]
    public void Constructor_RejectsNullClient()
    {
        Assert.Throws<ArgumentNullException>(() => new Pipeline(null!));
    }

    [Fact]
    public async Task RecordAsync_RecordsStepAndUpdatesCounts()
    {
        var (client, stub) = CreateClient((_, _) => FingerprintResponse("0fab"));
        using var pipeline = new Pipeline(client);

        FingerprintResult result = await pipeline.RecordAsync("step-1", new byte[] { 1 });

        Assert.Equal("0fab", result.RecordHex);
        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);

        PipelineReport report = pipeline.End();
        Assert.Equal(1, report.NSteps);
        Assert.Equal(0, report.NHits);
        Assert.Equal(1, report.NMisses);
        Assert.Single(report.Steps);
        Assert.Equal("step-1", report.Steps[0].Label);
        Assert.Equal("0fab", report.Steps[0].FingerprintHex);
        Assert.False(report.Steps[0].WasHit);
    }

    [Fact]
    public async Task RecordAsync_RejectsNullLabel()
    {
        var (client, _) = CreateClient((_, _) => FingerprintResponse("0fab"));
        using var pipeline = new Pipeline(client);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            pipeline.RecordAsync(null!, new byte[] { 1 }));
    }

    [Fact]
    public async Task RecordAsync_RejectsWhitespaceLabel()
    {
        var (client, _) = CreateClient((_, _) => FingerprintResponse("0fab"));
        using var pipeline = new Pipeline(client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            pipeline.RecordAsync("   ", new byte[] { 1 }));
    }

    [Fact]
    public async Task CachedAsync_HitPath_DoesNotCallCompute()
    {
        bool computeCalled = false;
        var (client, _) = CreateClient((_, _) => LookupHit(Encoding.UTF8.GetBytes("cached-value"), "deadbeef"));
        using var pipeline = new Pipeline(client);

        CachedResult<string> result = await pipeline.CachedAsync(
            "step-1",
            new byte[] { 1 },
            () =>
            {
                computeCalled = true;
                return Task.FromResult("computed");
            });

        Assert.False(computeCalled);
        Assert.True(result.WasHit);
        Assert.Equal("cached-value", result.Value);

        PipelineReport report = pipeline.End();
        Assert.Equal(1, report.NHits);
        Assert.Equal(0, report.NMisses);
        Assert.True(report.Steps[0].WasHit);
    }

    [Fact]
    public async Task CachedAsync_MissPath_ComputesAndStores()
    {
        var requests = new List<HttpRequestMessage>();
        byte[]? putBody = null;
        var (client, _) = CreateClient((req, _) =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("lookup", StringComparison.Ordinal))
            {
                return LookupMiss("cafe");
            }

            putBody = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return PutResponse();
        });
        using var pipeline = new Pipeline(client);

        bool computeCalled = false;
        CachedResult<string> result = await pipeline.CachedAsync(
            "step-1",
            new byte[] { 1 },
            () =>
            {
                computeCalled = true;
                return Task.FromResult("computed");
            },
            ttl: 120);

        Assert.True(computeCalled);
        Assert.False(result.WasHit);
        Assert.Equal("computed", result.Value);
        Assert.Equal(Encoding.UTF8.GetBytes("computed"), putBody);

        // Last request is the PUT to the lookup fingerprint.
        HttpRequestMessage put = requests[requests.Count - 1];
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.EndsWith("/v1/cache/cafe", put.RequestUri!.AbsolutePath);
        Assert.Equal("120", HeaderOrNull(put, "X-Hc-TTL"));
        Assert.Equal("step-1", HeaderOrNull(put, "X-Hc-Label"));

        PipelineReport report = pipeline.End();
        Assert.Equal(0, report.NHits);
        Assert.Equal(1, report.NMisses);
        Assert.False(report.Steps[0].WasHit);
    }

    [Fact]
    public async Task CachedAsync_MissPath_AttachesRun()
    {
        string? capturedRun = null;
        var (client, _) = CreateClient((req, _) =>
        {
            if (req.Method == HttpMethod.Put)
            {
                capturedRun = HeaderOrNull(req, "X-Hc-Run");
                return PutResponse();
            }

            return LookupMiss("cafe");
        });
        using var pipeline = new Pipeline(client, "exp-7");

        await pipeline.CachedAsync("step-1", new byte[] { 1 }, () => Task.FromResult("v"));

        Assert.Equal("exp-7", capturedRun);
    }

    [Fact]
    public async Task CachedAsync_RejectsNullLabel()
    {
        var (client, _) = CreateClient((_, _) => LookupMiss("cafe"));
        using var pipeline = new Pipeline(client);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            pipeline.CachedAsync(null!, new byte[] { 1 }, () => Task.FromResult("v")));
    }

    [Fact]
    public async Task CachedAsync_RejectsWhitespaceLabel()
    {
        var (client, _) = CreateClient((_, _) => LookupMiss("cafe"));
        using var pipeline = new Pipeline(client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            pipeline.CachedAsync("  ", new byte[] { 1 }, () => Task.FromResult("v")));
    }

    [Fact]
    public async Task CachedAsync_RejectsNullComputeFn()
    {
        var (client, _) = CreateClient((_, _) => LookupMiss("cafe"));
        using var pipeline = new Pipeline(client);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            pipeline.CachedAsync("step", new byte[] { 1 }, null!));
    }

    [Fact]
    public async Task End_ReportsAccurateCountsAndChain()
    {
        var (client, _) = CreateClient((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("fingerprint", StringComparison.Ordinal))
            {
                return FingerprintResponse("0fab");
            }

            if (req.Method == HttpMethod.Put)
            {
                return PutResponse();
            }

            return LookupHit(Encoding.UTF8.GetBytes("hit"), "deadbeef");
        });
        using var pipeline = new Pipeline(client);

        await pipeline.RecordAsync("a", new byte[] { 1 });
        await pipeline.CachedAsync("b", new byte[] { 2 }, () => Task.FromResult("v"));

        PipelineReport report = pipeline.End();

        Assert.Equal(2, report.NSteps);
        Assert.Equal(1, report.NHits);
        Assert.Equal(1, report.NMisses);
        Assert.True(report.TotalSeconds >= 0);
        Assert.Equal("deadbeef", report.Chain);
        Assert.Equal(2, report.Steps.Count);
    }

    [Fact]
    public async Task ExportAudit_ContainsLabelsAndSummary()
    {
        var (client, _) = CreateClient((_, _) => FingerprintResponse("0fab"));
        using var pipeline = new Pipeline(client);

        await pipeline.RecordAsync("step-1", new byte[] { 1 });
        await pipeline.RecordAsync("step-2", new byte[] { 2 });

        string audit = pipeline.End().ExportAudit();

        Assert.Contains("HyperCache Pipeline Report", audit);
        Assert.Contains("Steps: 2", audit);
        Assert.Contains("Misses: 2", audit);
        Assert.Contains("label=step-1", audit);
        Assert.Contains("label=step-2", audit);
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeClient()
    {
        var (client, _) = CreateClient((_, _) => FingerprintResponse("0fab"));
        var pipeline = new Pipeline(client);

        pipeline.Dispose();

        // The client should still be usable after the pipeline is disposed.
        FingerprintResult result = await client.FingerprintAsync(new byte[] { 1 });
        Assert.Equal("0fab", result.RecordHex);
    }
}
