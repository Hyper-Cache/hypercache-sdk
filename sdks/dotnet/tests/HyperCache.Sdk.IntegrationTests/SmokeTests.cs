using System;
using System.Text;
using System.Threading.Tasks;
using HyperCache;
using Xunit;

namespace HyperCache.IntegrationTests;

/// <summary>
/// Conservative live smoke tests. These are skipped unless <c>HYPERCACHE_KEY</c> is
/// set and never perform destructive operations (no bulk deletes).
/// </summary>
public sealed class SmokeTests
{
    [RequiresApiKeyFact]
    public async Task Fingerprint_SmallPayload_ReturnsNonEmptyHex()
    {
        using var client = new Client(new HyperCacheClientOptions
        {
            ApiKey = IntegrationTestSettings.ApiKey,
        });

        FingerprintResult result = await client.FingerprintAsync(
            Encoding.UTF8.GetBytes("hypercache-dotnet integration smoke test"));

        Assert.False(string.IsNullOrEmpty(result.RecordHex));
        Assert.NotEmpty(result.Record);
    }
}
