using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Admin;

/// <summary>
/// One event from the admin SSE stream (<c>GET /api/admin/stream</c>). Two families:
/// <c>snapshot</c> events carry the full fleet view in <see cref="Nodes"/> (also sent as a
/// ~10s keepalive), and <c>vector.*</c> lifecycle events (collection created/dropped, replica
/// assigned/lost, heal started/completed) carry <see cref="Sequence"/>, <see cref="Collection"/>
/// and <see cref="Data"/>.
/// </summary>
public sealed class AdminEvent
{
    /// <summary>SSE event name — <c>snapshot</c> or a <c>vector.*</c> kind.</summary>
    public string Event { get; init; } = string.Empty;

    /// <summary>Convenience flag — <c>true</c> for fleet snapshot events.</summary>
    public bool IsSnapshot => Nodes is not null;

    /// <summary>The fleet view, set on <c>snapshot</c> events only.</summary>
    public IReadOnlyList<AdminNode>? Nodes { get; init; }

    /// <summary>
    /// Monotonic sequence number, set on <c>vector.*</c> events. Increases per coordinator
    /// run; events are not replayed after a reconnect.
    /// </summary>
    public long? Sequence { get; init; }

    /// <summary>The collection the event concerns, when it concerns one.</summary>
    public string? Collection { get; init; }

    /// <summary>Server-side event time, set on <c>vector.*</c> events.</summary>
    public DateTimeOffset? AtUtc { get; init; }

    /// <summary>Event-specific extra fields (node ids, replica counts, …), verbatim.</summary>
    public JsonElement? Data { get; init; }
}

/// <summary>Payload of an SSE <c>snapshot</c> event: <c>{ "nodes": [...] }</c>.</summary>
internal sealed class AdminSnapshotPayload
{
    [JsonPropertyName("nodes")]
    public AdminNode[]? Nodes { get; set; }
}

/// <summary>Payload of an SSE <c>vector.*</c> event.</summary>
internal sealed class AdminVectorEventPayload
{
    [JsonPropertyName("sequence")]
    public long Sequence { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("collection")]
    public string? Collection { get; set; }

    [JsonPropertyName("atUtc")]
    public DateTimeOffset AtUtc { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}
