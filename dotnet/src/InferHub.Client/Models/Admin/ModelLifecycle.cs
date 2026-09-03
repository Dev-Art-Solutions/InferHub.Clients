using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Admin;

/// <summary>
/// The <c>202 Accepted</c> body every model-lifecycle call returns (pull, delete, warm, and the two
/// tool-model variants). Progress rides the existing <c>model-progress</c> SSE frame on
/// <see cref="IInferHubAdminClient.StreamAdminEventsAsync(System.Threading.CancellationToken)"/> —
/// this type does not invent a second way to ask how a command is going.
/// </summary>
public sealed class ModelCommandAccepted
{
    /// <summary>The node the command was sent to.</summary>
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>The model the command targets.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>The tool id, when this command targeted a tool's own model catalogue.</summary>
    [JsonPropertyName("tool")]
    public string? Tool { get; set; }

    /// <summary><c>pull</c>, <c>delete</c> or <c>warm</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>The id of the running command — poll the admin stream's <c>model-progress</c> frames for it.</summary>
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> when this call was coalesced onto a command already running for the same
    /// node/kind/model — the hub deduplicates rather than starting a second pull.
    /// </summary>
    [JsonPropertyName("reused")]
    public bool Reused { get; set; }

    /// <summary>Any additional fields the coordinator returns.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>Result of <c>GET /api/admin/models</c> — the fleet-wide model × node matrix.</summary>
public sealed class FleetModelMatrix
{
    /// <summary>Every connected node and what it can do with models.</summary>
    [JsonPropertyName("nodes")]
    public IReadOnlyList<FleetModelMatrixNode> Nodes { get; set; } = Array.Empty<FleetModelMatrixNode>();

    /// <summary>Every distinct model across the fleet and which nodes hold it.</summary>
    [JsonPropertyName("models")]
    public IReadOnlyList<FleetModel> Models { get; set; } = Array.Empty<FleetModel>();
}

/// <summary>One node's row in the fleet model matrix.</summary>
public sealed class FleetModelMatrixNode
{
    /// <summary>Node id.</summary>
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Human-friendly node name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether an admin has cordoned this node.</summary>
    [JsonPropertyName("cordoned")]
    public bool Cordoned { get; set; }

    /// <summary>Whether this node's backend can pull, delete or warm models at all.</summary>
    [JsonPropertyName("supportsModelManagement")]
    public bool SupportsModelManagement { get; set; }

    /// <summary>How many models this node currently advertises.</summary>
    [JsonPropertyName("modelCount")]
    public int ModelCount { get; set; }
}

/// <summary>One model's row in the fleet model matrix — which nodes hold it, and how large it is.</summary>
public sealed class FleetModel
{
    /// <summary>Model name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The largest reported size across holders, or <c>null</c> when no node reported one.</summary>
    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }

    /// <summary>Node ids that hold this model.</summary>
    [JsonPropertyName("nodes")]
    public IReadOnlyList<string> Nodes { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Result of <c>POST /api/admin/models/{model}/ensure</c> — the hub's placement decision, in full
/// (see this phase's D3: the "why" is not collapsed to a boolean).
/// </summary>
public sealed class EnsureModelResult
{
    /// <summary>The model that was ensured.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>The replica count that was asked for.</summary>
    [JsonPropertyName("requestedReplicas")]
    public int RequestedReplicas { get; set; }

    /// <summary>Nodes that already held the model before this call.</summary>
    [JsonPropertyName("alreadyPresent")]
    public IReadOnlyList<string> AlreadyPresent { get; set; } = Array.Empty<string>();

    /// <summary>Nodes a pull was just issued to.</summary>
    [JsonPropertyName("pulling")]
    public IReadOnlyList<EnsureModelPull> Pulling { get; set; } = Array.Empty<EnsureModelPull>();

    /// <summary>Whether the requested replica count is met by present-plus-pulling nodes.</summary>
    [JsonPropertyName("satisfied")]
    public bool Satisfied { get; set; }

    /// <summary>The hub's reasoning behind the decision above.</summary>
    [JsonPropertyName("decision")]
    public EnsureModelDecision Decision { get; set; } = new();
}

/// <summary>One node a pull was just issued to, as part of <see cref="EnsureModelResult"/>.</summary>
public sealed class EnsureModelPull
{
    /// <summary>The node the pull was issued to.</summary>
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>The node's human-friendly name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The id of the pull command — poll the admin stream's <c>model-progress</c> frames for it.</summary>
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    /// <summary>Whether this pull was coalesced onto one already running.</summary>
    [JsonPropertyName("reused")]
    public bool Reused { get; set; }
}

/// <summary>The hub's reasoning behind an <see cref="EnsureModelResult"/> — what was and was not eligible.</summary>
public sealed class EnsureModelDecision
{
    /// <summary>The replica count the hub actually targeted, after clamping to eligible nodes.</summary>
    [JsonPropertyName("effectiveTarget")]
    public int EffectiveTarget { get; set; }

    /// <summary>Nodes that hold the model but cannot manage models — they count toward the target but are never pulled onto.</summary>
    [JsonPropertyName("nonManageableHolders")]
    public IReadOnlyList<string> NonManageableHolders { get; set; } = Array.Empty<string>();

    /// <summary>Nodes that could have been pulled onto.</summary>
    [JsonPropertyName("eligibleCandidates")]
    public IReadOnlyList<string> EligibleCandidates { get; set; } = Array.Empty<string>();

    /// <summary>Nodes skipped because they are cordoned.</summary>
    [JsonPropertyName("cordonedNodesSkipped")]
    public IReadOnlyList<string> CordonedNodesSkipped { get; set; } = Array.Empty<string>();

    /// <summary>How far short of the target the fleet fell, if at all.</summary>
    [JsonPropertyName("shortfall")]
    public int Shortfall { get; set; }

    /// <summary>A human-readable summary of what the hub decided and why.</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}
