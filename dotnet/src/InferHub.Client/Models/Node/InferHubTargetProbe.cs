using InferHub.Client.Models;

namespace InferHub.Client.Models.Node;

/// <summary>What a <see cref="InferHubTargetProbe"/> found at the configured base address.</summary>
public enum InferHubTargetKind
{
    /// <summary>An InferHub coordinator — its <c>/api/status</c> carries no <c>mode</c> field.</summary>
    Hub,

    /// <summary>A solo InferHub node — its <c>/api/status</c> carries <c>mode: "solo"</c>.</summary>
    SoloNode
}

/// <summary>
/// The result of <see cref="IInferHubClient.ProbeAsync"/> — one <c>GET /api/status</c>, read once
/// and discriminated on the presence of <c>mode</c>. Exactly one of <see cref="HubStatus"/> /
/// <see cref="NodeStatus"/> is non-null, matching <see cref="Kind"/>.
/// </summary>
public sealed class InferHubTargetProbe
{
    /// <summary>Which kind of target answered.</summary>
    public required InferHubTargetKind Kind { get; init; }

    /// <summary>The coordinator or node version string, whichever answered.</summary>
    public string? Version { get; init; }

    /// <summary>The full hub document, when <see cref="Kind"/> is <see cref="InferHubTargetKind.Hub"/>.</summary>
    public StatusResponse? HubStatus { get; init; }

    /// <summary>The full node document, when <see cref="Kind"/> is <see cref="InferHubTargetKind.SoloNode"/>.</summary>
    public NodeStatusResponse? NodeStatus { get; init; }
}

/// <summary>Result of <c>GET /api/collections</c> (node-only) — every collection on this node.</summary>
internal sealed class NodeCollectionsResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("collections")]
    public IReadOnlyList<Admin.CollectionInfo> Collections { get; set; } = Array.Empty<Admin.CollectionInfo>();
}

/// <summary>Body for <c>POST /api/collections</c> (node-only).</summary>
internal sealed class NodeCreateCollectionRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("dimension")]
    public int Dimension { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("distance")]
    public string? Distance { get; set; }
}
