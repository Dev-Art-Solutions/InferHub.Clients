using System.Text.Json;
using System.Text.Json.Serialization;
using InferHub.Client.Models.Ollama;

namespace InferHub.Client.Models;

/// <summary>Response for <c>GET /api/status</c> — coordinator/fleet snapshot.</summary>
public sealed class StatusResponse
{
    /// <summary>Coordinator version string.</summary>
    [JsonPropertyName("coordinatorVersion")]
    public string? CoordinatorVersion { get; set; }

    /// <summary>Server-side timestamp when the snapshot was built.</summary>
    [JsonPropertyName("nowUtc")]
    public DateTimeOffset? NowUtc { get; set; }

    /// <summary>Coordinator uptime, in seconds.</summary>
    [JsonPropertyName("uptimeSeconds")]
    public double? UptimeSeconds { get; set; }

    /// <summary>Connected nodes with their live in-flight counts.</summary>
    [JsonPropertyName("nodes")]
    public IReadOnlyList<StatusNode>? Nodes { get; set; }

    /// <summary>Distinct models advertised across the fleet.</summary>
    [JsonPropertyName("models")]
    public IReadOnlyList<ModelInfo>? Models { get; set; }

    /// <summary>Coordinator-level metrics snapshot (shape depends on server version).</summary>
    [JsonPropertyName("metrics")]
    public JsonElement? Metrics { get; set; }

    /// <summary>Vector store block — present only when the coordinator has <c>VectorStore:Enabled=true</c>.</summary>
    [JsonPropertyName("vector")]
    public JsonElement? Vector { get; set; }

    /// <summary>Any additional fields the coordinator emits.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>One node in <see cref="StatusResponse.Nodes"/>.</summary>
public sealed class StatusNode
{
    /// <summary>Stable node id.</summary>
    [JsonPropertyName("nodeId")]
    public string? NodeId { get; set; }

    /// <summary>Human-friendly node name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Node-local Ollama endpoint (from the node's own configuration).</summary>
    [JsonPropertyName("ollamaEndpoint")]
    public string? OllamaEndpoint { get; set; }

    /// <summary>Node version string.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>Last time the node's heartbeat was seen.</summary>
    [JsonPropertyName("lastSeenUtc")]
    public DateTimeOffset? LastSeenUtc { get; set; }

    /// <summary>Seconds since the last heartbeat.</summary>
    [JsonPropertyName("ageSeconds")]
    public double? AgeSeconds { get; set; }

    /// <summary>Node-reported in-flight job count.</summary>
    [JsonPropertyName("inFlight")]
    public int? InFlight { get; set; }

    /// <summary>Coordinator-side in-flight counter for this node.</summary>
    [JsonPropertyName("localInFlight")]
    public int? LocalInFlight { get; set; }

    /// <summary>Number of models advertised by this node.</summary>
    [JsonPropertyName("modelCount")]
    public int? ModelCount { get; set; }

    /// <summary>Whether the node has been cordoned by an admin.</summary>
    [JsonPropertyName("cordoned")]
    public bool? Cordoned { get; set; }

    /// <summary>Any additional fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
