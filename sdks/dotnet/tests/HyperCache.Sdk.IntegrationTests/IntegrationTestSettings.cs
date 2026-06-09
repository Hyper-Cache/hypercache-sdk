using System;

namespace HyperCache.IntegrationTests;

/// <summary>
/// Gates the integration tests so they only run when a real HyperCache API key is
/// available via the <c>HYPERCACHE_KEY</c> environment variable. When the key is
/// absent, the tests are skipped so that normal local and CI test runs never call
/// the live API.
/// </summary>
internal static class IntegrationTestSettings
{
    /// <summary>
    /// Gets the API key from the <c>HYPERCACHE_KEY</c> environment variable, if set.
    /// </summary>
    public static string? ApiKey => Environment.GetEnvironmentVariable("HYPERCACHE_KEY");

    /// <summary>
    /// Gets a value indicating whether integration tests should run.
    /// </summary>
    public static bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// Gets the xUnit skip reason when integration tests are disabled, or
    /// <see langword="null"/> when they are enabled.
    /// </summary>
    public static string? SkipReason =>
        Enabled ? null : "HYPERCACHE_KEY is not set; skipping live integration tests.";
}
