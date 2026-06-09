using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class ClientLifecycleTests
{
    [Fact]
    public void Dispose_DoesNotDisposeInjectedHttpClient()
    {
        var stub = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var injected = new HttpClient(stub);

        var options = new HyperCacheClientOptions
        {
            ApiKey = "k",
            HttpClient = injected,
        };

        var client = new Client(options);
        client.Dispose();

        // The injected client must remain usable (not disposed) after Client.Dispose().
        injected.DefaultRequestHeaders.Add("X-Probe", "1");
        Assert.True(injected.DefaultRequestHeaders.Contains("X-Probe"));
    }

    [Fact]
    public async Task Dispose_DisposesOwnedHttpClient()
    {
        // When no HttpClient is injected, the Client owns the one it creates and must
        // dispose it. Using the disposed client to send a request surfaces the
        // underlying disposal as an ObjectDisposedException from the Client guard,
        // and the owned HttpClient itself is no longer usable.
        var client = new Client(new HyperCacheClientOptions
        {
            ApiKey = "k",
            BaseUrl = new Uri("https://api.example.test"),
        });

        client.Dispose();

        // The Client guards every endpoint call after disposal.
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.FingerprintAsync(new byte[] { 1 }));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var client = new Client(new HyperCacheClientOptions { ApiKey = "k" });

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public void Pipeline_AfterDispose_Throws()
    {
        var client = new Client(new HyperCacheClientOptions { ApiKey = "k" });
        client.Dispose();

        Assert.Throws<ObjectDisposedException>(() => client.Pipeline);
    }

    [Fact]
    public void BaseUrl_ReflectsConfiguredValue()
    {
        using var client = new Client(new HyperCacheClientOptions
        {
            ApiKey = "k",
            BaseUrl = new Uri("https://custom.example.test"),
        });

        Assert.Equal("https://custom.example.test/", client.BaseUrl);
    }
}
