using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Admin;

/// <summary>
/// A vector collection's definition and size, as returned by the admin plane.
/// </summary>
public sealed class CollectionInfo
{
    /// <summary>Collection name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Vector dimension every record in the collection must match.</summary>
    [JsonPropertyName("dimension")]
    public int Dimension { get; set; }

    /// <summary>Distance metric, e.g. <c>cosine</c>.</summary>
    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    /// <summary>Number of live records.</summary>
    [JsonPropertyName("recordCount")]
    public long RecordCount { get; set; }

    /// <summary>Total operations applied to the collection's ops log.</summary>
    [JsonPropertyName("operations")]
    public long Operations { get; set; }

    /// <summary>Any additional fields the coordinator returns.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>Replica placement for one collection — where its copies live on the fleet.</summary>
public sealed class CollectionPlacement
{
    /// <summary>Collection name.</summary>
    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>Configured replication factor.</summary>
    [JsonPropertyName("targetReplicas")]
    public int TargetReplicas { get; set; }

    /// <summary>Replicas currently held by connected nodes.</summary>
    [JsonPropertyName("liveReplicas")]
    public int LiveReplicas { get; set; }

    /// <summary>Node ids holding a replica right now.</summary>
    [JsonPropertyName("replicaNodes")]
    public IReadOnlyList<string> ReplicaNodes { get; set; } = Array.Empty<string>();
}

/// <summary>Query counters for one collection, from the coordinator's metrics.</summary>
public sealed class CollectionStats
{
    /// <summary>Collection name.</summary>
    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>Total queries served since coordinator start.</summary>
    [JsonPropertyName("queries")]
    public long Queries { get; set; }

    /// <summary>Average query latency in milliseconds.</summary>
    [JsonPropertyName("queryLatencyAvgMs")]
    public double QueryLatencyAvgMs { get; set; }
}

/// <summary>
/// Result of <c>GET /api/admin/vector/collections</c> — every collection plus its placement.
/// </summary>
public sealed class CollectionsResponse
{
    /// <summary>All collections in the store.</summary>
    [JsonPropertyName("collections")]
    public IReadOnlyList<CollectionInfo> Collections { get; set; } = Array.Empty<CollectionInfo>();

    /// <summary>Replica placement, one entry per collection.</summary>
    [JsonPropertyName("placement")]
    public IReadOnlyList<CollectionPlacement> Placement { get; set; } = Array.Empty<CollectionPlacement>();
}

/// <summary>
/// Result of <c>GET /api/admin/vector/collections/{collection}</c> — one collection's
/// definition, placement, replica health and query stats in a single read.
/// </summary>
public sealed class CollectionDetail
{
    /// <summary>The collection definition and size.</summary>
    [JsonPropertyName("collection")]
    public CollectionInfo Collection { get; set; } = new();

    /// <summary>Where the collection's replicas live.</summary>
    [JsonPropertyName("placement")]
    public CollectionPlacement Placement { get; set; } = new();

    /// <summary>
    /// <c>true</c> when fewer live replicas exist than the coordinator currently wants
    /// (replication factor capped by eligible node count).
    /// </summary>
    [JsonPropertyName("underReplicated")]
    public bool UnderReplicated { get; set; }

    /// <summary>Query counters for the collection.</summary>
    [JsonPropertyName("stats")]
    public CollectionStats? Stats { get; set; }
}

/// <summary>Body for <c>POST /api/admin/vector/collections</c>.</summary>
internal sealed class CreateCollectionRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("dimension")]
    public int Dimension { get; set; }

    [JsonPropertyName("distance")]
    public string? Distance { get; set; }
}
