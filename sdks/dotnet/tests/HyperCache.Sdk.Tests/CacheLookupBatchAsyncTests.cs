using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HyperCache.Tests;

public sealed class CacheLookupBatchAsyncTests : EndpointTestBase
{
    private static HttpResponseMessage TwoItemResponse()
    {
        // First item: a hit with value_b64; second item: a miss without value_b64.
        string valueB64 = Convert.ToBase64String(new byte[] { 200, 201 });
        string body =
            "{\"items\":[" +
            "{\"hit\":true,\"fingerprint_hex\":\"aa\",\"value_b64\":\"" + valueB64 +
            "\",\"expired\":false,\"size_bytes\":2,\"stored_at\":111,\"expires_at\":222}," +
            "{\"hit\":false,\"fingerprint_hex\":\"bb\",\"expired\":true}" +
            "]}";

        var response = Json(body);
        WithQuota(response);
        return response;
    }

    [Fact]
    public async Task UsesPostJsonToBatchEndpoint()
    {
        var (client, stub) = CreateClient((_, _) => TwoItemResponse());

        await client.CacheLookupBatchAsync(new[]
        {
            new CacheLookupBatchItem { Data = new byte[] { 1 } },
            new CacheLookupBatchItem { Data = new byte[] { 2 } },
        });

        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal(BaseUrl + "/v1/cache/lookup/batch", stub.LastRequest.RequestUri!.ToString());
        Assert.Equal("application/json", stub.LastRequest.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task EncodesDataAndOptionalFieldsInOrder()
    {
        string? bodyJson = null;
        var (client, _) = CreateClient((req, _) =>
        {
            bodyJson = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return TwoItemResponse();
        });

        await client.CacheLookupBatchAsync(new[]
        {
            new CacheLookupBatchItem
            {
                Data = new byte[] { 1, 2, 3 },
                Layers = 12,
                NTok = 40,
                Prev = new ReadOnlyMemory<byte>(new byte[] { 0x0F, 0xAB }),
            },
            new CacheLookupBatchItem
            {
                Data = new byte[] { 9 },
                PrevHex = "1234",
            },
        });

        using var doc = JsonDocument.Parse(bodyJson!);
        JsonElement items = doc.RootElement.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());

        JsonElement first = items[0];
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), first.GetProperty("data_b64").GetString());
        Assert.Equal(12, first.GetProperty("layers").GetInt32());
        Assert.Equal(40, first.GetProperty("n_tok").GetInt32());
        Assert.Equal("0fab", first.GetProperty("prev_hex").GetString());

        JsonElement second = items[1];
        Assert.Equal(Convert.ToBase64String(new byte[] { 9 }), second.GetProperty("data_b64").GetString());
        Assert.Equal("1234", second.GetProperty("prev_hex").GetString());
    }

    [Fact]
    public async Task PrevHexWinsOverPrevBytes()
    {
        string? bodyJson = null;
        var (client, _) = CreateClient((req, _) =>
        {
            bodyJson = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return TwoItemResponse();
        });

        await client.CacheLookupBatchAsync(new[]
        {
            new CacheLookupBatchItem
            {
                Data = new byte[] { 1 },
                Prev = new ReadOnlyMemory<byte>(new byte[] { 0xFF }),
                PrevHex = "abcd",
            },
        });

        using var doc = JsonDocument.Parse(bodyJson!);
        Assert.Equal("abcd", doc.RootElement.GetProperty("items")[0].GetProperty("prev_hex").GetString());
    }

    [Fact]
    public async Task ParsesHitAndMissItems()
    {
        var (client, _) = CreateClient((_, _) => TwoItemResponse());

        IReadOnlyList<BatchLookupResult> results = await client.CacheLookupBatchAsync(new[]
        {
            new CacheLookupBatchItem { Data = new byte[] { 1 } },
            new CacheLookupBatchItem { Data = new byte[] { 2 } },
        });

        Assert.Equal(2, results.Count);

        BatchLookupResult hit = results[0];
        Assert.True(hit.Hit);
        Assert.Equal("aa", hit.FingerprintHex);
        Assert.Equal(new byte[] { 200, 201 }, hit.Value);
        Assert.False(hit.Expired);
        Assert.Equal(2, hit.SizeBytes);
        Assert.Equal(111, hit.StoredAt);
        Assert.Equal(222, hit.ExpiresAt);

        BatchLookupResult miss = results[1];
        Assert.False(miss.Hit);
        Assert.Equal("bb", miss.FingerprintHex);
        Assert.Null(miss.Value);
        Assert.True(miss.Expired);
    }

    [Fact]
    public async Task AppliesQuotaHeadersToEveryItem()
    {
        var (client, _) = CreateClient((_, _) => TwoItemResponse());

        IReadOnlyList<BatchLookupResult> results = await client.CacheLookupBatchAsync(new[]
        {
            new CacheLookupBatchItem { Data = new byte[] { 1 } },
            new CacheLookupBatchItem { Data = new byte[] { 2 } },
        });

        foreach (BatchLookupResult result in results)
        {
            Assert.Equal(1.25, result.OpsUsed);
            Assert.Equal(1000.5, result.OpsCap);
            Assert.Equal(998.25, result.OpsRemaining);
        }
    }

    [Fact]
    public async Task NullInputsThrows()
    {
        var (client, _) = CreateClient((_, _) => TwoItemResponse());

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.CacheLookupBatchAsync(null!));
    }

    [Fact]
    public async Task EmptyInput_Throws_AndMakesNoHttpCall()
    {
        var (client, stub) = CreateClient((_, _) =>
            throw new InvalidOperationException("No HTTP call should be made for empty input."));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CacheLookupBatchAsync(Array.Empty<CacheLookupBatchItem>()));

        // The stub handler was never invoked, so no request was captured.
        Assert.Null(stub.LastRequest);
    }

    [Fact]
    public async Task DisposedClientThrows()
    {
        var (client, _) = CreateClient((_, _) => TwoItemResponse());
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.CacheLookupBatchAsync(Array.Empty<CacheLookupBatchItem>()));
    }
}
