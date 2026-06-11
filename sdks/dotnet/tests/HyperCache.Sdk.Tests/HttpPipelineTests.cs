using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HyperCache.Internal;
using Xunit;

namespace HyperCache.Tests;

public sealed class HttpPipelineTests
{
    private const string ApiKey = "test-key";

    private static HttpResponseMessage Respond(HttpStatusCode status, string body = "error body")
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
        };
    }

    private static (HttpPipeline Pipeline, StubHttpMessageHandler Handler) CreatePipeline(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        var stub = new StubHttpMessageHandler(handler);
        var httpClient = new HttpClient(stub)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        var options = new HyperCacheClientOptions
        {
            ApiKey = ApiKey,
            BaseUrl = new Uri("https://api.example.test"),
        };

        var pipeline = new HttpPipeline(httpClient, options, "1.2.3");
        return (pipeline, stub);
    }

    private static HttpRequestMessage Request() =>
        new HttpRequestMessage(HttpMethod.Get, "https://api.example.test/v1/fingerprint");

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(AuthException))]
    [InlineData(HttpStatusCode.PaymentRequired, typeof(QuotaException))]
    [InlineData((HttpStatusCode)429, typeof(RateLimitException))]
    [InlineData(HttpStatusCode.BadRequest, typeof(ClientException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(ClientException))]
    [InlineData(HttpStatusCode.NotFound, typeof(ClientException))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(ServerException))]
    [InlineData(HttpStatusCode.BadGateway, typeof(ServerException))]
    public async Task SendAsync_MapsStatusToException(HttpStatusCode status, Type expected)
    {
        var (pipeline, _) = CreatePipeline((_, _) => Respond(status));

        var ex = await Assert.ThrowsAsync(
            expected,
            () => pipeline.SendAsync(Request(), CancellationToken.None));

        Assert.IsType(expected, ex);
    }

    [Fact]
    public async Task SendAsync_PopulatesStatusOnClientAndServerExceptions()
    {
        var (clientPipeline, _) = CreatePipeline((_, _) => Respond(HttpStatusCode.BadRequest));
        var clientEx = await Assert.ThrowsAsync<ClientException>(
            () => clientPipeline.SendAsync(Request(), CancellationToken.None));
        Assert.Equal(400, clientEx.Status);

        var (serverPipeline, _) = CreatePipeline((_, _) => Respond(HttpStatusCode.BadGateway));
        var serverEx = await Assert.ThrowsAsync<ServerException>(
            () => serverPipeline.SendAsync(Request(), CancellationToken.None));
        Assert.Equal(502, serverEx.Status);
    }

    [Fact]
    public async Task SendAsync_IncludesResponseBodyInMessage()
    {
        var (pipeline, _) = CreatePipeline((_, _) => Respond(HttpStatusCode.BadRequest, "  bad request detail  "));

        var ex = await Assert.ThrowsAsync<ClientException>(
            () => pipeline.SendAsync(Request(), CancellationToken.None));

        Assert.Equal("bad request detail", ex.Message);
    }

    [Fact]
    public async Task SendAsync_AppliesAuthorizationAndUserAgentHeaders()
    {
        var (pipeline, stub) = CreatePipeline((_, _) => Respond(HttpStatusCode.OK, "ok"));

        using var response = await pipeline.SendAsync(Request(), CancellationToken.None);

        Assert.NotNull(stub.LastRequest);
        Assert.Equal("Bearer " + ApiKey, stub.LastRequest!.Headers.Authorization?.ToString());

        string userAgent = stub.LastRequest.Headers.UserAgent.ToString();
        Assert.StartsWith("hypercache-dotnet/", userAgent, StringComparison.Ordinal);
        Assert.Equal("hypercache-dotnet/1.2.3", userAgent);
    }

    [Fact]
    public async Task SendAsync_OmitsAuthorizationHeaderWhenApiKeyMissing()
    {
        var stub = new StubHttpMessageHandler((_, _) => Respond(HttpStatusCode.OK, "ok"));
        var httpClient = new HttpClient(stub)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        // No ApiKey configured: the pipeline must not attach an Authorization header.
        var options = new HyperCacheClientOptions
        {
            BaseUrl = new Uri("https://api.example.test"),
        };
        var pipeline = new HttpPipeline(httpClient, options, "1.2.3");

        using var response = await pipeline.SendAsync(Request(), CancellationToken.None);

        Assert.NotNull(stub.LastRequest);
        Assert.Null(stub.LastRequest!.Headers.Authorization);
        Assert.False(stub.LastRequest.Headers.Contains("Authorization"));

        // The User-Agent is still applied even without a key.
        Assert.StartsWith(
            "hypercache-dotnet/",
            stub.LastRequest.Headers.UserAgent.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendForBytesAsync_ReturnsBody()
    {
        var (pipeline, _) = CreatePipeline((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 }),
        });

        byte[] body = await pipeline.SendForBytesAsync(Request(), CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, body);
    }

    [Fact]
    public async Task SendForJsonAsync_DeserializesBody()
    {
        var (pipeline, _) = CreatePipeline((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"version\":7}"),
        });

        var result = await pipeline.SendForJsonAsync<VersionPayload>(Request(), CancellationToken.None);

        Assert.Equal(7, result.Version);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_MapsToServerException()
    {
        var (pipeline, _) = CreatePipeline((_, _) => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<ServerException>(
            () => pipeline.SendAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_CallerCancellation_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var (pipeline, _) = CreatePipeline((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Respond(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.SendAsync(Request(), cts.Token));
    }

    [Fact]
    public async Task SendAsync_Timeout_MapsToServerException()
    {
        var options = new HyperCacheClientOptions
        {
            ApiKey = ApiKey,
            BaseUrl = new Uri("https://api.example.test"),
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        // A handler that blocks until cancellation, forcing the pipeline timeout to fire.
        var handler = new DelayHandler();
        var httpClient = new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        var pipeline = new HttpPipeline(httpClient, options, "1.2.3");

        await Assert.ThrowsAsync<ServerException>(
            () => pipeline.SendAsync(Request(), CancellationToken.None));
    }

    private sealed class VersionPayload
    {
        public int Version { get; set; }
    }

    private sealed class DelayHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
