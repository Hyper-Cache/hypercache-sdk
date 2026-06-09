using Xunit;
using Xunit.Sdk;

namespace HyperCache.IntegrationTests;

/// <summary>
/// A <see cref="FactAttribute"/> that is skipped automatically unless the
/// <c>HYPERCACHE_KEY</c> environment variable is set. This keeps the live
/// integration tests out of normal local and CI test runs without requiring a
/// third-party skippable-fact package.
/// </summary>
[XunitTestCaseDiscoverer("Xunit.Sdk.FactDiscoverer", "xunit.execution.dotnet")]
public sealed class RequiresApiKeyFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiresApiKeyFactAttribute"/> class.
    /// </summary>
    public RequiresApiKeyFactAttribute()
    {
        if (!IntegrationTestSettings.Enabled)
        {
            Skip = IntegrationTestSettings.SkipReason;
        }
    }
}
