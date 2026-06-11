using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace HyperCache.Tests;

/// <summary>
/// Shared helpers for endpoint tests backed by a stubbed <see cref="HttpMessageHandler"/>.
/// </summary>
public abstract class EndpointTestBase
{
    protected const string BaseUrl = "https://api.example.test";

    protected static (Client Client, StubHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        var stub = new StubHttpMessageHandler(handler);
        var httpClient = new HttpClient(stub)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        var options = new HyperCacheClientOptions
        {
            ApiKey = "test-key",
            BaseUrl = new Uri(BaseUrl),
            HttpClient = httpClient,
        };

        return (new Client(options), stub);
    }

    protected static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    protected static HttpResponseMessage Bytes(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(body),
        };
    }

    protected static void WithQuota(HttpResponseMessage response)
    {
        response.Headers.TryAddWithoutValidation("X-Hc-Ops-Used", "1.25");
        response.Headers.TryAddWithoutValidation("X-Hc-Ops-Cap", "1000.5");
        response.Headers.TryAddWithoutValidation("X-Hc-Ops-Remaining", "998.25");
    }

    protected static string? HeaderOrNull(HttpRequestMessage? request, string name)
    {
        if (request is not null && request.Headers.TryGetValues(name, out var values))
        {
            foreach (string value in values)
            {
                return value;
            }
        }

        return null;
    }
}
