using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

/// <summary>
/// Tests for environment-variable-based client configuration
/// (<c>HYPERCACHE_KEY</c> and <c>HYPERCACHE_BASE_URL</c>).
/// </summary>
/// <remarks>
/// These tests mutate process environment variables, so they are grouped into a
/// non-parallel collection and always save/restore the previous values in a
/// <see langword="finally"/> block to avoid leaking state across tests.
/// </remarks>
[Collection(EnvironmentVariableTests.CollectionName)]
public sealed class EnvironmentVariableTests
{
    /// <summary>The xUnit collection name shared by environment-variable-mutating tests.</summary>
    public const string CollectionName = "Environment variables";

    private const string KeyVar = "HYPERCACHE_KEY";
    private const string BaseUrlVar = "HYPERCACHE_BASE_URL";

    [Fact]
    public void DefaultClient_ReadsApiKeyFromEnvironment()
    {
        RunWithEnvironment(
            key: "env-key",
            baseUrl: null,
            () =>
            {
                // No explicit ApiKey: the key must be resolved from HYPERCACHE_KEY and applied
                // as a Bearer Authorization header on outbound requests.
                Assert.Equal("Bearer env-key", CapturedAuthorizationWithoutExplicitKey());
            });
    }

    [Fact]
    public void DefaultClient_ReadsBaseUrlFromEnvironment()
    {
        RunWithEnvironment(
            key: "env-key",
            baseUrl: "https://env.example.test",
            () =>
            {
                using var client = new Client();

                Assert.Equal("https://env.example.test/", client.BaseUrl);
            });
    }

    [Fact]
    public void ExplicitApiKey_OverridesEnvironmentKey()
    {
        RunWithEnvironment(
            key: "env-key",
            baseUrl: null,
            () =>
            {
                Assert.Equal(
                    "Bearer explicit-key",
                    CapturedAuthorization(new HyperCacheClientOptions { ApiKey = "explicit-key" }));
            });
    }

    [Fact]
    public void ExplicitBaseUrl_OverridesEnvironmentBaseUrl()
    {
        RunWithEnvironment(
            key: "env-key",
            baseUrl: "https://env.example.test",
            () =>
            {
                using var client = new Client(new HyperCacheClientOptions
                {
                    BaseUrl = new Uri("https://explicit.example.test"),
                });

                Assert.Equal("https://explicit.example.test/", client.BaseUrl);
            });
    }

    [Fact]
    public void MissingApiKey_ThrowsAuthException()
    {
        RunWithEnvironment(
            key: null,
            baseUrl: null,
            () =>
            {
                AuthException ex = Assert.Throws<AuthException>(() => new Client());
                Assert.Equal(401, ex.Status);
                Assert.Contains("HYPERCACHE_KEY", ex.Message, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void MissingApiKey_WithExplicitOptions_ThrowsAuthException()
    {
        RunWithEnvironment(
            key: null,
            baseUrl: null,
            () =>
            {
                Assert.Throws<AuthException>(() => new Client(new HyperCacheClientOptions()));
            });
    }

    [Fact]
    public async Task StaticClient_DoesNotBypassMissingKeyBehavior()
    {
        // The static convenience surface uses a lazily initialized default Client built via
        // the same environment-based configuration path. With no key available, it must fail
        // fast with AuthException rather than silently creating an unauthenticated client.
        await RunWithEnvironmentAsync(
            key: null,
            baseUrl: null,
            async () =>
            {
                Exception ex = await Record.ExceptionAsync(
                    () => HyperCacheClient.FingerprintAsync(new byte[] { 1 }));

                // Lazy<T> wraps the initialization exception; the AuthException is either the
                // exception itself or its inner exception.
                Assert.True(
                    ex is AuthException || ex?.InnerException is AuthException,
                    $"Expected an AuthException from the static path but got: {ex}");
            });
    }

    /// <summary>
    /// Builds a client from the supplied options over a stub handler, issues one request, and
    /// returns the Authorization header the pipeline applied. The handler short-circuits before
    /// any real network I/O.
    /// </summary>
    private static string? CapturedAuthorization(HyperCacheClientOptions options)
    {
        string? authorization = null;
        var stub = new StubHttpMessageHandler((req, _) =>
        {
            authorization = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"fingerprint_hex\":\"00\",\"version\":1}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        });

        using var httpClient = new HttpClient(stub) { Timeout = Timeout.InfiniteTimeSpan };
        options.HttpClient = httpClient;

        using var client = new Client(options);
        client.FingerprintAsync(new byte[] { 1 }).GetAwaiter().GetResult();
        return authorization;
    }

    /// <summary>
    /// Same as <see cref="CapturedAuthorization"/> but constructs the client with no explicit
    /// API key, forcing resolution from the <c>HYPERCACHE_KEY</c> environment variable.
    /// </summary>
    private static string? CapturedAuthorizationWithoutExplicitKey() =>
        CapturedAuthorization(new HyperCacheClientOptions());

    private static void RunWithEnvironment(string? key, string? baseUrl, Action body)
    {
        string? previousKey = Environment.GetEnvironmentVariable(KeyVar);
        string? previousBaseUrl = Environment.GetEnvironmentVariable(BaseUrlVar);
        try
        {
            Environment.SetEnvironmentVariable(KeyVar, key);
            Environment.SetEnvironmentVariable(BaseUrlVar, baseUrl);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeyVar, previousKey);
            Environment.SetEnvironmentVariable(BaseUrlVar, previousBaseUrl);
        }
    }

    private static async Task RunWithEnvironmentAsync(string? key, string? baseUrl, Func<Task> body)
    {
        string? previousKey = Environment.GetEnvironmentVariable(KeyVar);
        string? previousBaseUrl = Environment.GetEnvironmentVariable(BaseUrlVar);
        try
        {
            Environment.SetEnvironmentVariable(KeyVar, key);
            Environment.SetEnvironmentVariable(BaseUrlVar, baseUrl);
            await body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeyVar, previousKey);
            Environment.SetEnvironmentVariable(BaseUrlVar, previousBaseUrl);
        }
    }
}
