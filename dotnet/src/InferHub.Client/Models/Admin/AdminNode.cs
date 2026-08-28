using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Admin;

/// <summary>
/// A connected node as seen by the admin plane — the shape returned by
/// <c>GET /api/admin/nodes</c> and inside the SSE <c>snapshot</c> events.
/// </summary>
public sealed class AdminNode
{
    /// <summary>Coordinator-side connection id (changes on reconnect).</summary>
    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>Stable node id — the handle used for cordon/uncordon/deregister.</summary>
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Human-friendly node name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The node's local inference backend endpoint (e.g. its Ollama URL).</summary>
    [JsonPropertyName("ollamaEndpoint")]
    public string OllamaEndpoint { get; set; } = string.Empty;

    /// <summary>Node software version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Last heartbeat, in UTC.</summary>
    [JsonPropertyName("lastSeenUtc")]
    public DateTimeOffset LastSeenUtc { get; set; }

    /// <summary>Seconds since the last heartbeat.</summary>
    [JsonPropertyName("ageSeconds")]
    public double AgeSeconds { get; set; }

    /// <summary>Jobs currently routed to this node by the coordinator.</summary>
    [JsonPropertyName("inFlight")]
    public int InFlight { get; set; }

    /// <summary>In-flight count reported by the node itself.</summary>
    [JsonPropertyName("localInFlight")]
    public int LocalInFlight { get; set; }

    /// <summary>Number of models the node advertises.</summary>
    [JsonPropertyName("modelCount")]
    public int ModelCount { get; set; }

    /// <summary>Operator labels attached at enrollment.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyDictionary<string, string>? Labels { get; set; }

    /// <summary>Node concurrency cap, when one is configured.</summary>
    [JsonPropertyName("maxConcurrency")]
    public int? MaxConcurrency { get; set; }

    /// <summary>Whether the node is cordoned — excluded from routing but still connected.</summary>
    [JsonPropertyName("cordoned")]
    public bool Cordoned { get; set; }

    /// <summary>The most recent audited admin action on this node, when any.</summary>
    [JsonPropertyName("lastAction")]
    public AdminNodeAction? LastAction { get; set; }

    /// <summary>Any additional fields the coordinator returns.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>An audited admin action (cordon/uncordon/deregister) recorded against a node.</summary>
public sealed class AdminNodeAction
{
    /// <summary>Action name, e.g. <c>cordon</c>, <c>uncordon</c>, <c>deregister</c>.</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>When the action was recorded, in UTC.</summary>
    [JsonPropertyName("atUtc")]
    public DateTimeOffset AtUtc { get; set; }

    /// <summary>Who performed the action (<c>local</c>, an IP, or <c>admin</c>).</summary>
    [JsonPropertyName("by")]
    public string By { get; set; } = string.Empty;
}
