using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Admin;

/// <summary>
/// What the coordinator says a node should be doing (phase 43 on the hub): capabilities on or off,
/// tools on or off, models pulled or removed, concurrency lowered, a corpus assigned. Used both to
/// read a profile back and to write one — see <see cref="IInferHubAdminClient.PutProfileAsync"/> for
/// why <see cref="Name"/> and <see cref="Revision"/> are ignored on write.
/// </summary>
public sealed class NodeProfile
{
    /// <summary>Profile name. Ignored on write — the route segment wins.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Monotonic per profile, bumped by the hub on every write. Ignored on write.</summary>
    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    /// <summary>Which nodes this profile applies to. Required — an empty selector is refused by the hub.</summary>
    [JsonPropertyName("selector")]
    public NodeProfileSelector Selector { get; set; } = new();

    /// <summary>Capability kind → whether the node should serve it. <c>false</c> narrows only.</summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyDictionary<string, bool>? Capabilities { get; set; }

    /// <summary>Tool id → whether the node should run it. <c>false</c> narrows only.</summary>
    [JsonPropertyName("tools")]
    public IReadOnlyDictionary<string, bool>? Tools { get; set; }

    /// <summary>Models the node should hold or drop.</summary>
    [JsonPropertyName("models")]
    public NodeProfileModels? Models { get; set; }

    /// <summary>Lowered, never raised. Null leaves the node's own cap alone.</summary>
    [JsonPropertyName("maxConcurrency")]
    public int? MaxConcurrency { get; set; }

    /// <summary>The corpus this node should be running, if any.</summary>
    [JsonPropertyName("retrieval")]
    public RetrievalProfile? Retrieval { get; set; }

    /// <summary>Image recipe id → whether the node should offer it. <c>false</c> narrows only.</summary>
    [JsonPropertyName("imageRecipes")]
    public IReadOnlyDictionary<string, bool>? ImageRecipes { get; set; }

    /// <summary>Any additional fields the coordinator returns.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>
/// Which nodes a profile applies to: an exact node id, or an exact match on every label pair given.
/// A selector naming nothing matches nothing on the hub — never everything.
/// </summary>
public sealed class NodeProfileSelector
{
    /// <summary>An exact node id, or <c>null</c> to match by label alone.</summary>
    [JsonPropertyName("nodeId")]
    public string? NodeId { get; set; }

    /// <summary>Every pair here must match exactly, or <c>null</c> to match by node id alone.</summary>
    [JsonPropertyName("labels")]
    public IReadOnlyDictionary<string, string>? Labels { get; set; }
}

/// <summary>Models a profile wants a node to hold, and models it wants removed.</summary>
public sealed class NodeProfileModels
{
    /// <summary>Model names the node should pull if it does not already hold them.</summary>
    [JsonPropertyName("ensure")]
    public IReadOnlyList<string>? Ensure { get; set; }

    /// <summary>Model names the node should remove if it holds them.</summary>
    [JsonPropertyName("remove")]
    public IReadOnlyList<string>? Remove { get; set; }
}

/// <summary>A corpus the coordinator wants a node to host.</summary>
public sealed class RetrievalProfile
{
    /// <summary>Whether the node should be running its own corpus at all.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary><c>local</c> or <c>qdrant</c>. <c>postgres</c> is refused by the hub on a node.</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>Where the engine is, for an external provider. Ignored by <c>local</c>.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// A name the node resolves against its own configuration — never a key. See
    /// <c>IInferHubAdminClient</c>'s remarks on why this client does nothing else with the string.
    /// </summary>
    [JsonPropertyName("credentialRef")]
    public string? CredentialRef { get; set; }

    /// <summary>Collections this node owns and the hub has recorded itself out of.</summary>
    [JsonPropertyName("collections")]
    public IReadOnlyList<string>? Collections { get; set; }

    /// <summary>Model used to embed this node's own corpus.</summary>
    [JsonPropertyName("embeddingModel")]
    public string? EmbeddingModel { get; set; }
}

/// <summary>One thing a node would not do, and the reason in the operator's words.</summary>
public sealed class NodeProfileRefusal
{
    /// <summary>What the node refused — a capability, a tool, a concurrency value.</summary>
    [JsonPropertyName("item")]
    public string Item { get; set; } = string.Empty;

    /// <summary>Why, naming the configuration key that stopped it.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Result of <c>GET /api/admin/nodes/{nodeId}/profile</c> — what one node is actually running
/// against what a profile asked for.
/// </summary>
public sealed class NodeProfileState
{
    /// <summary>The node this state describes.</summary>
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>The node's human-friendly name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The profile name assigned by selector match, if any.</summary>
    [JsonPropertyName("assigned")]
    public string? Assigned { get; set; }

    /// <summary>The assigned profile's revision, when one is assigned.</summary>
    [JsonPropertyName("revision")]
    public long? Revision { get; set; }

    /// <summary>Two or more profiles matched this node; names of the conflicting ones.</summary>
    [JsonPropertyName("conflicts")]
    public IReadOnlyList<string>? Conflicts { get; set; }

    /// <summary><c>applied</c> | <c>refused</c> | <c>pending</c> | <c>none</c> | <c>conflict</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>What the node is actually resolved to right now, independent of any profile.</summary>
    [JsonPropertyName("effective")]
    public NodeProfileEffective? Effective { get; set; }

    /// <summary>Human-readable, one line per item that took effect.</summary>
    [JsonPropertyName("applied")]
    public IReadOnlyList<string>? Applied { get; set; }

    /// <summary>What the node would not do, and why. The load-bearing field.</summary>
    [JsonPropertyName("refusals")]
    public IReadOnlyList<NodeProfileRefusal>? Refusals { get; set; }

    /// <summary>Started and still running — a model pull, typically. Progress rides the admin SSE stream.</summary>
    [JsonPropertyName("pending")]
    public IReadOnlyList<string>? Pending { get; set; }

    /// <summary>When the node last reported this state.</summary>
    [JsonPropertyName("reportedAtUtc")]
    public DateTimeOffset? ReportedAtUtc { get; set; }

    /// <summary>Any additional fields the coordinator returns.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>What a node resolves to right now — desired beside effective (hub 45 D1).</summary>
public sealed class NodeProfileEffective
{
    /// <summary>Capability kinds this node will actually be routed for.</summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();

    /// <summary>The concurrency cap the node is actually registered at after clamping.</summary>
    [JsonPropertyName("maxConcurrency")]
    public int? MaxConcurrency { get; set; }
}

/// <summary>Result of <c>PUT /api/admin/profiles/{name}</c> — the stored profile plus who it reached.</summary>
public sealed class PutProfileResult
{
    /// <summary>The profile as stored, with the hub's own <c>name</c> and <c>revision</c>.</summary>
    [JsonPropertyName("profile")]
    public NodeProfile Profile { get; set; } = new();

    /// <summary>Node ids this profile now applies to.</summary>
    [JsonPropertyName("applied")]
    public IReadOnlyList<string> Applied { get; set; } = Array.Empty<string>();

    /// <summary>Nodes that matched more than one profile, and the names they matched.</summary>
    [JsonPropertyName("conflicts")]
    public IReadOnlyList<ProfileConflict> Conflicts { get; set; } = Array.Empty<ProfileConflict>();
}

/// <summary>One node matched by more than one profile — the hub applies neither and reports both.</summary>
public sealed class ProfileConflict
{
    /// <summary>The node with more than one matching profile.</summary>
    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>The names of the profiles that matched.</summary>
    [JsonPropertyName("profiles")]
    public IReadOnlyList<string> Profiles { get; set; } = Array.Empty<string>();
}

/// <summary>Result of <c>DELETE /api/admin/profiles/{name}</c>.</summary>
public sealed class DeleteProfileResult
{
    /// <summary>The name of the profile that was deleted.</summary>
    [JsonPropertyName("deleted")]
    public string Deleted { get; set; } = string.Empty;

    /// <summary>Nodes reverted to their own configuration now that this profile is gone.</summary>
    [JsonPropertyName("reverted")]
    public IReadOnlyList<string> Reverted { get; set; } = Array.Empty<string>();
}
