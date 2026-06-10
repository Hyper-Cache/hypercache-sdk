using System;
using System.Net.Http;

namespace HyperCache;

/// <summary>
/// Configures the HyperCache SDK client.
/// </summary>
public sealed class HyperCacheClientOptions
{
    /// <summary>The base URL used when neither an explicit value nor HYPERCACHE_BASE_URL is provided.</summary>
    internal static readonly Uri DefaultBaseUrl = new("https://api.hypercache.ai");

    private Uri _baseUrl = DefaultBaseUrl;

    /// <summary>
    /// Gets or sets the HyperCache API key. If omitted, the SDK reads the
    /// <c>HYPERCACHE_KEY</c> environment variable.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the HyperCache API base URL. If left at its default and the
    /// <c>HYPERCACHE_BASE_URL</c> environment variable is set, that value is used instead.
    /// </summary>
    public Uri BaseUrl
    {
        get => _baseUrl;
        set
        {
            _baseUrl = value ?? throw new ArgumentNullException(nameof(value));
            BaseUrlExplicitlySet = true;
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="BaseUrl"/> was explicitly assigned by the caller.
    /// When <see langword="false"/>, the SDK may fall back to the <c>HYPERCACHE_BASE_URL</c> environment variable.
    /// </summary>
    internal bool BaseUrlExplicitlySet { get; private set; }

    /// <summary>
    /// Gets or sets the request timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets an optional externally managed HTTP client.
    /// </summary>
    public HttpClient? HttpClient { get; set; }
}
