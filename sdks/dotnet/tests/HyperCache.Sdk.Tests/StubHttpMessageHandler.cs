using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HyperCache.Tests;

/// <summary>
/// A hand-rolled <see cref="HttpMessageHandler"/> test double that returns a
/// caller-supplied response and captures the most recent outbound request.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

    public StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>Gets the most recent request observed by the handler.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(_handler(request, cancellationToken));
    }
}
