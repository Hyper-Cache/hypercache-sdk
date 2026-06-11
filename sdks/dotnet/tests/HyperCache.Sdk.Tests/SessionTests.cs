using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class SessionTests : EndpointTestBase
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

    private static HttpResponseMessage ListResponse()
    {
        var response = Json("{\"bucket\":\"today\",\"part\":\"ALL\",\"total_count\":0,\"total_bytes\":0,\"runs\":[]}");
        WithQuota(response);
        return response;
    }

    [Fact]
    public void Constructor_RejectsNullClient()
    {
        Assert.Throws<ArgumentNullException>(() => new Session(null!));
    }

    [Fact]
    public async Task Reset_ClearsPrev()
    {
        var (client, _) = CreateClient((_, _) => FingerprintResponse("0fab"));
        var session = new Session(client);

        await session.FingerprintAsync(new byte[] { 1 });
        Assert.NotNull(session.Prev);

        session.Reset();

        Assert.Null(session.Prev);
    }

    [Fact]
    public async Task FingerprintAsync_UpdatesPrevAfterSuccess()
    {
        var (client, _) = CreateClient((_, _) => FingerprintResponse("0fab"));
        var session = new Session(client);

        await session.FingerprintAsync(new byte[] { 1 });

        Assert.Equal(new byte[] { 0x0F, 0xAB }, session.Prev);
    }

    [Fact]
    public async Task FingerprintAsync_SendsPrevOnSecondCall()
    {
        var requests = new List<string?>();
        var (client, _) = CreateClient((req, _) =>
        {
            requests.Add(HeaderOrNull(req, "X-Hc-Prev"));
            return FingerprintResponse("0fab");
        });
        var session = new Session(client);

        await session.FingerprintAsync(new byte[] { 1 });
        await session.FingerprintAsync(new byte[] { 2 });

        Assert.Null(requests[0]);
        Assert.Equal("0fab", requests[1]);
    }

    [Fact]
    public async Task FingerprintAsync_DoesNotOverrideExplicitPrevHex()
    {
        var requests = new List<string?>();
        var (client, _) = CreateClient((req, _) =>
        {
            requests.Add(HeaderOrNull(req, "X-Hc-Prev"));
            return FingerprintResponse("0fab");
        });
        var session = new Session(client);

        await session.FingerprintAsync(new byte[] { 1 });
        await session.FingerprintAsync(
            new byte[] { 2 },
            new FingerprintOptions { PrevHex = "1234" });

        Assert.Equal("1234", requests[1]);
    }

    [Fact]
    public async Task CacheLookupAsync_SendsPrevAfterFingerprint()
    {
        var requests = new List<string?>();
        var (client, _) = CreateClient((req, _) =>
        {
            requests.Add(HeaderOrNull(req, "X-Hc-Prev"));
            if (req.RequestUri!.AbsolutePath.EndsWith("fingerprint", StringComparison.Ordinal))
            {
                return FingerprintResponse("0fab");
            }

            return LookupMiss("0fab");
        });
        var session = new Session(client);

        await session.FingerprintAsync(new byte[] { 1 });
        await session.CacheLookupAsync(new byte[] { 2 });

        Assert.Equal("0fab", requests[1]);
    }

    [Fact]
    public async Task CacheLookupAsync_UpdatesPrevFromFingerprintHex()
    {
        var (client, _) = CreateClient((_, _) => LookupMiss("cafe"));
        var session = new Session(client);

        await session.CacheLookupAsync(new byte[] { 1 });

        Assert.Equal(new byte[] { 0xCA, 0xFE }, session.Prev);
    }

    [Fact]
    public async Task CacheLookupAsync_UpdatesPrevOnHit()
    {
        var (client, _) = CreateClient((_, _) => LookupHit(new byte[] { 9 }, "deadbeef"));
        var session = new Session(client);

        await session.CacheLookupAsync(new byte[] { 1 });

        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, session.Prev);
    }

    [Fact]
    public async Task CachePutAsync_AttachesRunWhenNotSpecified()
    {
        string? capturedRun = null;
        var (client, _) = CreateClient((req, _) =>
        {
            capturedRun = HeaderOrNull(req, "X-Hc-Run");
            return PutResponse();
        });
        var session = new Session(client);

        await session.WithRunAsync("run-1", async s =>
        {
            await s.CachePutAsync("abcd", new byte[] { 1, 2, 3 });
        });

        Assert.Equal("run-1", capturedRun);
    }

    [Fact]
    public async Task CachePutAsync_DoesNotOverrideExplicitRun()
    {
        string? capturedRun = null;
        var (client, _) = CreateClient((req, _) =>
        {
            capturedRun = HeaderOrNull(req, "X-Hc-Run");
            return PutResponse();
        });
        var session = new Session(client);

        await session.WithRunAsync("run-1", async s =>
        {
            await s.CachePutAsync(
                "abcd",
                new byte[] { 1 },
                new CachePutOptions { Run = "explicit" });
        });

        Assert.Equal("explicit", capturedRun);
    }

    [Fact]
    public async Task CachePutAsync_PreservesTtlAndLabelWhenAttachingRun()
    {
        string? ttl = null;
        string? label = null;
        var (client, _) = CreateClient((req, _) =>
        {
            ttl = HeaderOrNull(req, "X-Hc-TTL");
            label = HeaderOrNull(req, "X-Hc-Label");
            return PutResponse();
        });
        var session = new Session(client);

        await session.WithRunAsync("run-1", async s =>
        {
            await s.CachePutAsync(
                "abcd",
                new byte[] { 1 },
                new CachePutOptions { Ttl = 60, Label = "step" });
        });

        Assert.Equal("60", ttl);
        Assert.Equal("step", label);
    }

    [Fact]
    public async Task CacheListAsync_AttachesRunWhenNotSpecified()
    {
        string? capturedQuery = null;
        var (client, _) = CreateClient((req, _) =>
        {
            capturedQuery = req.RequestUri!.Query;
            return ListResponse();
        });
        var session = new Session(client);

        await session.WithRunAsync("run-1", async s =>
        {
            await s.CacheListAsync();
        });

        Assert.Contains("run=run-1", capturedQuery);
    }

    [Fact]
    public async Task CacheListAsync_DoesNotOverrideExplicitRun()
    {
        string? capturedQuery = null;
        var (client, _) = CreateClient((req, _) =>
        {
            capturedQuery = req.RequestUri!.Query;
            return ListResponse();
        });
        var session = new Session(client);

        await session.WithRunAsync("run-1", async s =>
        {
            await s.CacheListAsync(new CacheListOptions { Run = "explicit" });
        });

        Assert.Contains("run=explicit", capturedQuery);
    }

    [Fact]
    public async Task WithRunAsync_Generic_SetsRunDuringCallbackAndRestores()
    {
        var (client, _) = CreateClient((_, _) => PutResponse());
        var session = new Session(client);

        Assert.Null(session.Run);

        string observed = await session.WithRunAsync("scoped", s =>
        {
            Assert.Equal("scoped", s.Run);
            return Task.FromResult("done");
        });

        Assert.Equal("done", observed);
        Assert.Null(session.Run);
    }

    [Fact]
    public async Task WithRunAsync_Generic_RestoresRunAfterException()
    {
        var (client, _) = CreateClient((_, _) => PutResponse());
        var session = new Session(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.WithRunAsync<int>("scoped", _ => throw new InvalidOperationException()));

        Assert.Null(session.Run);
    }

    [Fact]
    public async Task WithRunAsync_NonGeneric_SetsRunDuringCallbackAndRestores()
    {
        var (client, _) = CreateClient((_, _) => PutResponse());
        var session = new Session(client);

        await session.WithRunAsync("scoped", s =>
        {
            Assert.Equal("scoped", s.Run);
            return Task.CompletedTask;
        });

        Assert.Null(session.Run);
    }

    [Fact]
    public async Task WithRunAsync_NonGeneric_RestoresRunAfterException()
    {
        var (client, _) = CreateClient((_, _) => PutResponse());
        var session = new Session(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.WithRunAsync("scoped", _ => throw new InvalidOperationException()));

        Assert.Null(session.Run);
    }

    [Fact]
    public async Task WithRunAsync_RejectsNullRun()
    {
        var (client, _) = CreateClient((_, _) => PutResponse());
        var session = new Session(client);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            session.WithRunAsync(null!, _ => Task.CompletedTask));
    }

    [Fact]
    public async Task WithRunAsync_RejectsWhitespaceRun()
    {
        var (client, _) = CreateClient((_, _) => PutResponse());
        var session = new Session(client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            session.WithRunAsync("   ", _ => Task.CompletedTask));
    }

    [Fact]
    public async Task WithRunAsync_RejectsNullAction()
    {
        var (client, _) = CreateClient((_, _) => PutResponse());
        var session = new Session(client);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            session.WithRunAsync("run", (Func<Session, Task>)null!));
    }

    [Fact]
    public async Task StringOverload_FingerprintUsesUtf8()
    {
        byte[]? captured = null;
        var (client, _) = CreateClient((req, _) =>
        {
            captured = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return FingerprintResponse("0fab");
        });
        var session = new Session(client);

        await session.FingerprintAsync("héllo");

        Assert.Equal(Encoding.UTF8.GetBytes("héllo"), captured);
    }
}
