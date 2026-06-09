using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HyperCache.Internal;

/// <summary>
/// Centralizes HyperCache HTTP behavior: header application, request dispatch,
/// timeout/cancellation handling, body reading, and status-to-exception mapping.
/// </summary>
/// <remarks>
/// Per-endpoint special cases (such as treating <c>404</c> as a cache miss) are
/// intentionally left to callers; this pipeline maps all non-success statuses to
/// the SDK exception hierarchy via <see cref="SendAsync"/> unless the caller opts
/// out by handling the response itself.
/// </remarks>
internal sealed class HttpPipeline
{
    private readonly HttpClient _httpClient;
    private readonly HyperCacheClientOptions _options;
    private readonly string _userAgent;
    private readonly Uri _baseAddress;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpPipeline"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to send requests.</param>
    /// <param name="options">The client options (base URL, API key, timeout).</param>
    /// <param name="packageVersion">The SDK package version used in the User-Agent header.</param>
    public HttpPipeline(HttpClient httpClient, HyperCacheClientOptions options, string packageVersion)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrEmpty(packageVersion))
        {
            packageVersion = "0.0.0";
        }

        _userAgent = "hypercache-dotnet/" + packageVersion;

        // Normalize to a trailing slash so relative endpoint paths combine correctly.
        string baseUrl = _options.BaseUrl.ToString();
        if (baseUrl.Length == 0 || baseUrl[baseUrl.Length - 1] != '/')
        {
            baseUrl += "/";
        }

        _baseAddress = new Uri(baseUrl, UriKind.Absolute);
    }

    /// <summary>
    /// Builds an absolute request URI from a relative endpoint path (for example,
    /// <c>v1/fingerprint</c>). The leading slash is optional.
    /// </summary>
    /// <param name="relativePath">The endpoint path relative to the base URL.</param>
    /// <returns>The absolute request URI.</returns>
    public Uri BuildUri(string relativePath)
    {
        if (relativePath is null)
        {
            throw new ArgumentNullException(nameof(relativePath));
        }

        string trimmed = relativePath.Length > 0 && relativePath[0] == '/'
            ? relativePath.Substring(1)
            : relativePath;

        return new Uri(_baseAddress, trimmed);
    }

    /// <summary>
    /// Sends a request, applying SDK headers and timeout, and maps non-success
    /// statuses to the SDK exception hierarchy.
    /// </summary>
    /// <param name="request">The request to send. The pipeline applies auth and user-agent headers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The successful response. The caller owns and must dispose it.</returns>
    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ApplyHeaders(request);

        HttpResponseMessage response = await SendCoreAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // Read the body for the error message, then translate the status code.
        string body;
        try
        {
            body = await ReadStringAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            response.Dispose();
            throw;
        }
#pragma warning disable CA1031 // Never let body-read failures mask the underlying HTTP error.
        catch
        {
            body = string.Empty;
        }
#pragma warning restore CA1031

        int status = (int)response.StatusCode;
        response.Dispose();
        throw MapStatusToException(status, body);
    }

    /// <summary>
    /// Sends a request, applying SDK headers and timeout, but allows the supplied
    /// status codes to pass through as a successful response instead of being mapped
    /// to an exception. Used by endpoints with per-call status semantics (such as
    /// treating <c>404</c> as a cache miss).
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="allowedStatus">A status code (besides 2xx) to treat as success.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The response. The caller owns and must dispose it.</returns>
    public async Task<HttpResponseMessage> SendAllowingStatusAsync(
        HttpRequestMessage request,
        int allowedStatus,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ApplyHeaders(request);

        HttpResponseMessage response = await SendCoreAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode || (int)response.StatusCode == allowedStatus)
        {
            return response;
        }

        string body;
        try
        {
            body = await ReadStringAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            response.Dispose();
            throw;
        }
#pragma warning disable CA1031 // Never let body-read failures mask the underlying HTTP error.
        catch
        {
            body = string.Empty;
        }
#pragma warning restore CA1031

        int status = (int)response.StatusCode;
        response.Dispose();
        throw MapStatusToException(status, body);
    }

    /// <summary>
    /// Sends a request and returns the response body as bytes.
    /// </summary>
    public async Task<byte[]> SendForBytesAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request and deserializes the JSON response body to <typeparamref name="T"/>.
    /// </summary>
    public async Task<T> SendForJsonAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        byte[] payload = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);

        return Deserialize<T>(payload);
    }

    /// <summary>
    /// Sends a request, deserializes the JSON response body to <typeparamref name="T"/>,
    /// and also returns the parsed quota headers from the response.
    /// </summary>
    public async Task<(T Value, QuotaHeaders Quota)> SendForJsonWithQuotaAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        QuotaHeaders quota = QuotaHeaders.From(response);
        byte[] payload = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);

        return (Deserialize<T>(payload), quota);
    }

    /// <summary>
    /// Deserializes a JSON payload to <typeparamref name="T"/>, mapping failures to
    /// <see cref="ServerException"/>.
    /// </summary>
    public static T Deserialize<T>(byte[] payload)
    {
        try
        {
            T? result = JsonSerializer.Deserialize<T>(payload, JsonDefaults.Options);
            if (result is null)
            {
                throw new ServerException("HyperCache returned an empty or null JSON response.");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new ServerException("Failed to parse the HyperCache JSON response.", null, ex);
        }
    }

    /// <summary>
    /// Serializes a value to UTF-8 JSON bytes using the shared SDK options.
    /// </summary>
    public static byte[] Serialize<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);

    /// <summary>
    /// Reads a response body as bytes, mapping read failures to <see cref="ServerException"/>.
    /// </summary>
    public static async Task<byte[]> ReadBytesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        try
        {
#if NET8_0_OR_GREATER
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
            cancellationToken.ThrowIfCancellationRequested();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new ServerException("Failed to read the HyperCache response body.", null, ex);
        }
        catch (IOException ex)
        {
            throw new ServerException("Failed to read the HyperCache response body.", null, ex);
        }
    }

    /// <summary>
    /// Reads a response body as a string.
    /// </summary>
    public static async Task<string> ReadStringAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

#if NET8_0_OR_GREATER
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        cancellationToken.ThrowIfCancellationRequested();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// Maps an HTTP status code and optional response body to an SDK exception.
    /// </summary>
    public static HyperCacheException MapStatusToException(int status, string? body)
    {
        string message = string.IsNullOrWhiteSpace(body)
            ? $"HyperCache request failed with status {status}."
            : body!.Trim();

        return status switch
        {
            401 => new AuthException(message),
            402 => new QuotaException(message),
            429 => new RateLimitException(message),
            >= 500 => new ServerException(message, status),
            >= 400 => new ClientException(message, status),
            _ => new ServerException(message, status),
        };
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        if (_options.Timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(_options.Timeout);
        }

        try
        {
            return await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            // Preserve caller cancellation; convert timeouts to ServerException.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (timeoutCts.IsCancellationRequested)
            {
                throw new ServerException(
                    $"HyperCache request timed out after {_options.Timeout.TotalSeconds:0.###}s.",
                    null,
                    ex);
            }

            throw new ServerException("HyperCache request was canceled unexpectedly.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ServerException("HyperCache request failed: " + ex.Message, null, ex);
        }
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _options.ApiKey);
        }

        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
    }
}
