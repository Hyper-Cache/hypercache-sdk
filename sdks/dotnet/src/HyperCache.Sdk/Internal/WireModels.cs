using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HyperCache.Internal;

/// <summary>
/// Wire response for <c>POST /v1/fingerprint</c>.
/// </summary>
internal sealed class FingerprintResponse
{
    [JsonPropertyName("fingerprint_hex")]
    public string? FingerprintHex { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; }
}

/// <summary>
/// Wire response for <c>PUT /v1/cache/{fp}</c>.
/// </summary>
internal sealed class CachePutResponse
{
    [JsonPropertyName("stored")]
    public bool Stored { get; set; }

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("run")]
    public string? Run { get; set; }
}

/// <summary>
/// Wire response for a <c>POST /v1/cache/lookup</c> miss.
/// </summary>
internal sealed class CacheLookupMissResponse
{
    [JsonPropertyName("fingerprint_hex")]
    public string? FingerprintHex { get; set; }

    [JsonPropertyName("expired")]
    public bool Expired { get; set; }
}

/// <summary>
/// Wire request for <c>POST /v1/cache/lookup/batch</c>.
/// </summary>
internal sealed class BatchLookupRequest
{
    [JsonPropertyName("items")]
    public List<BatchLookupRequestItem> Items { get; set; } = new List<BatchLookupRequestItem>();
}

/// <summary>
/// A single item in a batch lookup request.
/// </summary>
internal sealed class BatchLookupRequestItem
{
    [JsonPropertyName("data_b64")]
    public string DataB64 { get; set; } = string.Empty;

    [JsonPropertyName("layers")]
    public int? Layers { get; set; }

    [JsonPropertyName("n_tok")]
    public int? NTok { get; set; }

    [JsonPropertyName("prev_hex")]
    public string? PrevHex { get; set; }
}

/// <summary>
/// Wire response for <c>POST /v1/cache/lookup/batch</c>.
/// </summary>
internal sealed class BatchLookupResponse
{
    [JsonPropertyName("items")]
    public List<BatchLookupResponseItem> Items { get; set; } = new List<BatchLookupResponseItem>();
}

/// <summary>
/// A single item in a batch lookup response.
/// </summary>
internal sealed class BatchLookupResponseItem
{
    [JsonPropertyName("hit")]
    public bool Hit { get; set; }

    [JsonPropertyName("fingerprint_hex")]
    public string? FingerprintHex { get; set; }

    [JsonPropertyName("value_b64")]
    public string? ValueB64 { get; set; }

    [JsonPropertyName("expired")]
    public bool Expired { get; set; }

    [JsonPropertyName("size_bytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("stored_at")]
    public long? StoredAt { get; set; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; set; }
}

/// <summary>
/// Wire response for <c>GET /v1/cache/list</c>.
/// </summary>
internal sealed class CacheListWireResponse
{
    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    [JsonPropertyName("part")]
    public string? Part { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("total_bytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("runs")]
    public List<CacheListWireRunGroup> Runs { get; set; } = new List<CacheListWireRunGroup>();

    [JsonPropertyName("next_cursor")]
    public int? NextCursor { get; set; }
}

/// <summary>
/// A single run group in a <c>GET /v1/cache/list</c> response.
/// </summary>
internal sealed class CacheListWireRunGroup
{
    [JsonPropertyName("run")]
    public string? Run { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("total_bytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("entries")]
    public List<CacheListWireEntry> Entries { get; set; } = new List<CacheListWireEntry>();
}

/// <summary>
/// A single entry within a <c>GET /v1/cache/list</c> run group.
/// </summary>
internal sealed class CacheListWireEntry
{
    [JsonPropertyName("fingerprint_hex")]
    public string? FingerprintHex { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("run")]
    public string? Run { get; set; }

    [JsonPropertyName("size_bytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("stored_at")]
    public long? StoredAt { get; set; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; set; }
}

/// <summary>
/// Wire request for <c>POST /v1/cache/{fp}/relabel</c>.
/// </summary>
/// <remarks>
/// Relabel supports explicit null-clears: a <see langword="null"/> value for
/// <c>label</c> or <c>run</c> clears that field. The shared serializer options
/// omit nulls when writing, so this request is serialized with a dedicated
/// options instance that preserves null values.
/// </remarks>
internal sealed class CacheRelabelWireRequest
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("run")]
    public string? Run { get; set; }
}

/// <summary>
/// Wire response for <c>POST /v1/cache/{fp}/relabel</c>.
/// </summary>
internal sealed class CacheRelabelWireResponse
{
    [JsonPropertyName("relabeled")]
    public bool Relabeled { get; set; }

    [JsonPropertyName("fingerprint_hex")]
    public string? FingerprintHex { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("run")]
    public string? Run { get; set; }
}

/// <summary>
/// Wire response for <c>DELETE /v1/cache/by-label</c> and <c>DELETE /v1/cache/by-age</c>.
/// </summary>
internal sealed class BulkDeleteWireResponse
{
    [JsonPropertyName("deleted")]
    public long Deleted { get; set; }

    [JsonPropertyName("bytes_freed")]
    public long BytesFreed { get; set; }

    [JsonPropertyName("cutoff_unix")]
    public long? CutoffUnix { get; set; }
}
