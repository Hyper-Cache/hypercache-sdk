using System;
using System.Threading.Tasks;
using HyperCache;
using Xunit;

namespace HyperCache.IntegrationTests;

/// <summary>
/// Verifies the SDK produces the exact records published at
/// https://api.hypercache.ai/v1/parity.json, proving the client is correct
/// end-to-end (not merely that it returns something). Each published vector is
/// fingerprinted through the SDK's own FingerprintAsync and asserted byte-for-byte
/// against its expected record. Skipped unless HYPERCACHE_KEY is set.
/// </summary>
public sealed class ParityTests
{
    // (input_b64, expected_record_hex) from /v1/parity.json, codec v2, default headers.
    private static readonly (string InputB64, string ExpectedHex)[] Vectors =
    {
        ("SHlwZXJDYWNoZSBwYXJpdHkgdmVjdG9yIEEgLS0gZGV0ZXJtaW5pc3RpYyB0ZXN0IGlucHV0",
         "0200000000000000000000000000000000000000000000000000000000000000690000000d000000e64324a0f1f06e9f000000000d000000690000007ee3b386e23c135eb0c93d633735597e00000000000000008d56da480000"),
        ("MDEyMzQ1Njc4OWFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVotIShAIyQlXiYqKCkrPSx7fVtdfDp7fSI8Pj8vfn4=",
         "0200000000000000000000000000000000000000000000000000000000000000ae000000fcffffffae9975e58083091500000000fcffffffae0000007ee3b386e23c135eeed595fc5faf31d300000000000000006864732e0000"),
        ("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5fYGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6e3x9fn8=",
         "0200000000000000000000000000000000000000000000000000000000000000f0000000000000001db5068238488c5e0000000000000000f00000007ee3b386e23c135ec2d13df1b6617e820000000000000000be36ecc00000"),
    };

    /// <summary>
    /// Each published parity vector, fingerprinted through the SDK, must return its
    /// exact expected record.
    /// </summary>
    [RequiresApiKeyFact]
    public async Task Fingerprint_MatchesPublishedParityVectors()
    {
        using var client = new Client(new HyperCacheClientOptions
        {
            ApiKey = IntegrationTestSettings.ApiKey,
        });

        foreach ((string inputB64, string expectedHex) in Vectors)
        {
            byte[] input = Convert.FromBase64String(inputB64);
            FingerprintResult result = await client.FingerprintAsync(input);
            Assert.Equal(expectedHex, result.RecordHex);
        }
    }
}
