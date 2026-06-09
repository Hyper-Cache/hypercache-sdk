using System;
using System.Net.Http;

namespace HyperCache;

/// <summary>
/// Configures the HyperCache SDK client.
/// </summary>
public sealed class HyperCacheClientOptions
{
    /// <summary>
    /// Gets or sets the HyperCache API key. If omitted, the SDK will later read HYPERCACHE_KEY.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the HyperCache API base URL.
    /// </summary>
    public Uri BaseUrl { get; set; } = new Uri("https://api.hypercache.ai");

    /// <summary>
    /// Gets or sets the request timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets an optional externally managed HTTP client.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}
