using System.Text.Json;
using System.Text.Json.Serialization;
using InferHub.Client.Models.Ollama;

namespace InferHub.Client.Models.Node;

/// <summary>
/// A solo node's <c>GET /api/status</c> — deliberately a different, smaller document than the
/// hub's <see cref="StatusResponse"/>. <c>Mode</c> is always <c>"solo"</c> and is the only field
/// that tells the two documents apart; there is no fleet array, no queue block and no replica
/// count, because a node with no coordinator has no concept of any of them.
/// </summary>
public sealed class NodeStatusResponse
{
    /// <summary>Always <c>"solo"</c>. The discriminator <see cref="InferHubClient.ProbeAsync"/> reads.</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>Node version string.</summary>
    [JsonPropertyName("nodeVersion")]
    public string? NodeVersion { get; set; }

    /// <summary>Server-side timestamp when the snapshot was built.</summary>
    [JsonPropertyName("nowUtc")]
    public DateTimeOffset? NowUtc { get; set; }

    /// <summary>The node's own configured name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The inference backend this node runs.</summary>
    [JsonPropertyName("backend")]
    public NodeBackendInfo? Backend { get; set; }

    /// <summary>Concurrency gate state, or <c>null</c> when the node runs with no gate configured.</summary>
    [JsonPropertyName("concurrency")]
    public NodeConcurrency? Concurrency { get; set; }

    /// <summary>What this process can see of the local GPU — not where a given model landed.</summary>
    [JsonPropertyName("gpu")]
    public NodeGpuInfo? Gpu { get; set; }

    /// <summary>What this node will and will not answer — the same declaration a meshed node sends the hub.</summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string>? Capabilities { get; set; }

    /// <summary>The node's local retrieval corpus, or <c>{ enabled: false }</c> when there is none.</summary>
    [JsonPropertyName("retrieval")]
    public NodeRetrievalInfo? Retrieval { get; set; }

    /// <summary>Models advertised by this node's backend.</summary>
    [JsonPropertyName("models")]
    public IReadOnlyList<ModelInfo>? Models { get; set; }

    /// <summary>Any additional fields the node emits.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>The inference backend a solo node runs, from <see cref="NodeStatusResponse.Backend"/>.</summary>
public sealed class NodeBackendInfo
{
    /// <summary>Backend name, e.g. <c>ollama</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The backend's own endpoint, as configured on this node.</summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    /// <summary>
    /// Supervised health (<c>"healthy"</c>/<c>"unhealthy"</c>/…), or <c>null</c> when nothing is
    /// supervising the backend. Absence is reported as absence, never as healthy.
    /// </summary>
    [JsonPropertyName("health")]
    public string? Health { get; set; }
}

/// <summary>The node's local concurrency gate, from <see cref="NodeStatusResponse.Concurrency"/>.</summary>
public sealed class NodeConcurrency
{
    /// <summary>Configured concurrent-job capacity.</summary>
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    /// <summary>Jobs in flight right now.</summary>
    [JsonPropertyName("inFlight")]
    public int InFlight { get; set; }
}

/// <summary>What this node's own process can see of the local GPU, from <see cref="NodeStatusResponse.Gpu"/>.</summary>
public sealed class NodeGpuInfo
{
    /// <summary>Whether CUDA devices are visible to this process.</summary>
    [JsonPropertyName("cuda")]
    public bool Cuda { get; set; }

    /// <summary>Number of visible CUDA devices.</summary>
    [JsonPropertyName("devices")]
    public int Devices { get; set; }

    /// <summary>Device names, when any are visible.</summary>
    [JsonPropertyName("names")]
    public IReadOnlyList<string>? Names { get; set; }
}

/// <summary>The node's local retrieval corpus, from <see cref="NodeStatusResponse.Retrieval"/>.</summary>
public sealed class NodeRetrievalInfo
{
    /// <summary><c>false</c> for a node with no corpus running — the reason a retrieve header gets a <c>501</c>.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Vector store provider backing the corpus, when enabled.</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>Default embedding model for this corpus, when enabled.</summary>
    [JsonPropertyName("embeddingModel")]
    public string? EmbeddingModel { get; set; }

    /// <summary>Retrieval mode, when enabled.</summary>
    [JsonPropertyName("mode")]
    public string? RetrievalMode { get; set; }

    /// <summary>Whether reranking is on, when enabled.</summary>
    [JsonPropertyName("rerank")]
    public bool? Rerank { get; set; }

    /// <summary>The corpus's own collections, when enabled.</summary>
    [JsonPropertyName("collections")]
    public IReadOnlyList<NodeRetrievalCollection>? Collections { get; set; }

    /// <summary>The last error the corpus failed to start with, when it could not.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// One collection as reported inline on <see cref="NodeRetrievalInfo.Collections"/>. This is a
/// smaller shape than <see cref="Admin.CollectionInfo"/> (no <c>operations</c> counter, and the
/// record count is named <c>records</c> here) — it is the status snapshot, not the lifecycle
/// response; use <see cref="IInferHubClient.GetNodeCollectionAsync"/> for the latter.
/// </summary>
public sealed class NodeRetrievalCollection
{
    /// <summary>Collection name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Vector dimension every record in the collection must match.</summary>
    [JsonPropertyName("dimension")]
    public int Dimension { get; set; }

    /// <summary>Distance metric, e.g. <c>cosine</c>.</summary>
    [JsonPropertyName("distance")]
    public string? Distance { get; set; }

    /// <summary>Number of live records.</summary>
    [JsonPropertyName("records")]
    public long Records { get; set; }
}
