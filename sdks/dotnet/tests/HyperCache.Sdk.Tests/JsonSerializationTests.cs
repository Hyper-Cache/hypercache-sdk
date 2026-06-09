using System.Text;
using System.Text.Json;
using HyperCache.Internal;
using Xunit;

namespace HyperCache.Tests;

/// <summary>
/// Verifies snake_case wire mapping for the internal DTOs, using the shared SDK
/// serializer options. These tests pin the JSON property names so that a field
/// rename or attribute removal is caught immediately.
/// </summary>
public sealed class JsonSerializationTests
{
    private static T Deserialize<T>(string json) =>
        HttpPipeline.Deserialize<T>(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void FingerprintResponse_MapsFingerprintHex()
    {
        var result = Deserialize<FingerprintResponse>(
            "{\"fingerprint_hex\":\"abcd\",\"version\":5}");

        Assert.Equal("abcd", result.FingerprintHex);
        Assert.Equal(5, result.Version);
    }

    [Fact]
    public void CachePutResponse_MapsSizeBytesAndExpiresAt()
    {
        var result = Deserialize<CachePutResponse>(
            "{\"stored\":true,\"size_bytes\":42,\"expires_at\":1712345678,\"label\":\"L\",\"run\":\"R\"}");

        Assert.True(result.Stored);
        Assert.Equal(42, result.SizeBytes);
        Assert.Equal(1712345678, result.ExpiresAt);
        Assert.Equal("L", result.Label);
        Assert.Equal("R", result.Run);
    }

    [Fact]
    public void CacheLookupMissResponse_MapsFingerprintHex()
    {
        var result = Deserialize<CacheLookupMissResponse>(
            "{\"fingerprint_hex\":\"cafe\",\"expired\":true}");

        Assert.Equal("cafe", result.FingerprintHex);
        Assert.True(result.Expired);
    }

    [Fact]
    public void BatchLookupRequestItem_WritesSnakeCaseAndOmitsNulls()
    {
        var item = new BatchLookupRequestItem
        {
            DataB64 = "ZGF0YQ==",
            Layers = 12,
            NTok = 40,
            PrevHex = "1234",
        };

        string json = Encoding.UTF8.GetString(HttpPipeline.Serialize(item));

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal("ZGF0YQ==", root.GetProperty("data_b64").GetString());
        Assert.Equal(12, root.GetProperty("layers").GetInt32());
        Assert.Equal(40, root.GetProperty("n_tok").GetInt32());
        Assert.Equal("1234", root.GetProperty("prev_hex").GetString());
    }

    [Fact]
    public void BatchLookupRequestItem_OmitsNullOptionalFields()
    {
        var item = new BatchLookupRequestItem { DataB64 = "ZGF0YQ==" };

        string json = Encoding.UTF8.GetString(HttpPipeline.Serialize(item));

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.False(root.TryGetProperty("layers", out _));
        Assert.False(root.TryGetProperty("n_tok", out _));
        Assert.False(root.TryGetProperty("prev_hex", out _));
    }

    [Fact]
    public void BatchLookupResponseItem_MapsAllSnakeCaseFields()
    {
        var result = Deserialize<BatchLookupResponseItem>(
            "{\"hit\":true,\"fingerprint_hex\":\"aa\",\"value_b64\":\"dg==\"," +
            "\"expired\":false,\"size_bytes\":2,\"stored_at\":111,\"expires_at\":222}");

        Assert.True(result.Hit);
        Assert.Equal("aa", result.FingerprintHex);
        Assert.Equal("dg==", result.ValueB64);
        Assert.False(result.Expired);
        Assert.Equal(2, result.SizeBytes);
        Assert.Equal(111, result.StoredAt);
        Assert.Equal(222, result.ExpiresAt);
    }

    [Fact]
    public void CacheListWireResponse_MapsSnakeCaseAndEntries()
    {
        var result = Deserialize<CacheListWireResponse>(
            "{\"bucket\":\"today\",\"part\":\"ALL\",\"total_count\":1,\"total_bytes\":123," +
            "\"runs\":[{\"run\":\"r\",\"count\":1,\"total_bytes\":123,\"entries\":[" +
            "{\"fingerprint_hex\":\"abc\",\"size_bytes\":123,\"stored_at\":1,\"expires_at\":2," +
            "\"label\":\"L\",\"run\":\"r\"}]}],\"next_cursor\":42}");

        Assert.Equal("today", result.Bucket);
        Assert.Equal("ALL", result.Part);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(123, result.TotalBytes);
        Assert.Equal(42, result.NextCursor);

        Assert.Single(result.Runs);
        CacheListWireRunGroup group = result.Runs[0];
        Assert.Equal("r", group.Run);
        Assert.Equal(1, group.Count);
        Assert.Equal(123, group.TotalBytes);

        Assert.Single(group.Entries);
        CacheListWireEntry entry = group.Entries[0];
        Assert.Equal("abc", entry.FingerprintHex);
        Assert.Equal(123, entry.SizeBytes);
        Assert.Equal(1, entry.StoredAt);
        Assert.Equal(2, entry.ExpiresAt);
        Assert.Equal("L", entry.Label);
        Assert.Equal("r", entry.Run);
    }

    [Fact]
    public void CacheListWireResponse_NullNextCursorParsesAsNull()
    {
        var result = Deserialize<CacheListWireResponse>(
            "{\"bucket\":\"today\",\"part\":\"ALL\",\"total_count\":0,\"total_bytes\":0," +
            "\"runs\":[],\"next_cursor\":null}");

        Assert.Null(result.NextCursor);
    }

    [Fact]
    public void CacheRelabelWireRequest_IncludeNullsWritesExplicitNulls()
    {
        var request = new CacheRelabelWireRequest { Label = null, Run = null };

        string json = Encoding.UTF8.GetString(HttpPipeline.SerializeIncludingNulls(request));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("label").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("run").ValueKind);
    }

    [Fact]
    public void CacheRelabelWireRequest_IncludeNullsWritesValues()
    {
        var request = new CacheRelabelWireRequest { Label = "L", Run = "R" };

        string json = Encoding.UTF8.GetString(HttpPipeline.SerializeIncludingNulls(request));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("L", doc.RootElement.GetProperty("label").GetString());
        Assert.Equal("R", doc.RootElement.GetProperty("run").GetString());
    }

    [Fact]
    public void CacheRelabelWireResponse_MapsSnakeCaseFields()
    {
        var result = Deserialize<CacheRelabelWireResponse>(
            "{\"relabeled\":true,\"fingerprint_hex\":\"abc\",\"label\":\"L\",\"run\":\"R\"}");

        Assert.True(result.Relabeled);
        Assert.Equal("abc", result.FingerprintHex);
        Assert.Equal("L", result.Label);
        Assert.Equal("R", result.Run);
    }

    [Fact]
    public void BulkDeleteWireResponse_MapsBytesFreedAndCutoffUnix()
    {
        var result = Deserialize<BulkDeleteWireResponse>(
            "{\"deleted\":10,\"bytes_freed\":2048,\"cutoff_unix\":1712340000}");

        Assert.Equal(10, result.Deleted);
        Assert.Equal(2048, result.BytesFreed);
        Assert.Equal(1712340000, result.CutoffUnix);
    }

    [Fact]
    public void BulkDeleteWireResponse_OptionalCutoffUnixDefaultsToNull()
    {
        var result = Deserialize<BulkDeleteWireResponse>(
            "{\"deleted\":1,\"bytes_freed\":2}");

        Assert.Null(result.CutoffUnix);
    }
}
