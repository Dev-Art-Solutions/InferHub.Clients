using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Admin;

/// <summary>
/// Result of <c>GET /api/admin/usage</c> — aggregates only, never content (hub 25 D3). See this
/// phase's D4 for why <see cref="UsageRow"/> models the wire rather than the hub's richer internal
/// <c>UsageAggregate</c> type: the audio/character/image/video unit totals are not serialized onto
/// this route today.
/// </summary>
public sealed class UsageResponse
{
    /// <summary>The lower bound that was queried, echoed back.</summary>
    [JsonPropertyName("from")]
    public DateTimeOffset? From { get; set; }

    /// <summary>The upper bound that was queried, echoed back.</summary>
    [JsonPropertyName("to")]
    public DateTimeOffset? To { get; set; }

    /// <summary>One row per distinct (client, model) pair matching the query.</summary>
    [JsonPropertyName("rows")]
    public IReadOnlyList<UsageRow> Rows { get; set; } = Array.Empty<UsageRow>();
}

/// <summary>One aggregated usage row — a client, a model, and token/request counts.</summary>
public sealed class UsageRow
{
    /// <summary>The client this row is attributed to.</summary>
    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The model this row is attributed to.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Number of requests aggregated into this row.</summary>
    [JsonPropertyName("requests")]
    public long Requests { get; set; }

    /// <summary>Prompt tokens consumed.</summary>
    [JsonPropertyName("promptTokens")]
    public long PromptTokens { get; set; }

    /// <summary>Completion tokens produced.</summary>
    [JsonPropertyName("completionTokens")]
    public long CompletionTokens { get; set; }

    /// <summary>Prompt plus completion tokens.</summary>
    [JsonPropertyName("totalTokens")]
    public long TotalTokens { get; set; }

    /// <summary>How many of these requests were served by a cloud provider rather than the fleet.</summary>
    [JsonPropertyName("fallbackRequests")]
    public long FallbackRequests { get; set; }

    /// <summary>Any additional fields the coordinator returns.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>
/// One row of <c>GET /api/admin/clients</c> — a configured client's ids and limits, plus its live
/// window consumption. Never a key (this phase's D5): <c>ClientConfig.Key</c> never leaves the hub.
/// </summary>
public sealed class ClientRow
{
    /// <summary>The client id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary><c>null</c> for a client with no configured limits — unlimited, not zero.</summary>
    [JsonPropertyName("limits")]
    public ClientRowLimits? Limits { get; set; }

    /// <summary>What this client has consumed against its windows right now.</summary>
    [JsonPropertyName("live")]
    public ClientLiveUsage Live { get; set; } = new();
}

/// <summary>A client's configured limits. Every field nullable — <c>null</c> means unlimited.</summary>
public sealed class ClientRowLimits
{
    /// <summary>Maximum concurrent in-flight requests.</summary>
    [JsonPropertyName("maxConcurrent")]
    public int? MaxConcurrent { get; set; }

    /// <summary>Maximum requests per rolling minute.</summary>
    [JsonPropertyName("requestsPerMinute")]
    public int? RequestsPerMinute { get; set; }

    /// <summary>Maximum tokens per rolling minute.</summary>
    [JsonPropertyName("tokensPerMinute")]
    public long? TokensPerMinute { get; set; }

    /// <summary>Maximum tokens per UTC day.</summary>
    [JsonPropertyName("tokensPerDay")]
    public long? TokensPerDay { get; set; }

    /// <summary><c>null</c>/absent means every model.</summary>
    [JsonPropertyName("allowedModels")]
    public IReadOnlyList<string>? AllowedModels { get; set; }
}

/// <summary>A client's live window consumption right now.</summary>
public sealed class ClientLiveUsage
{
    /// <summary>Requests currently in flight for this client.</summary>
    [JsonPropertyName("inFlight")]
    public int InFlight { get; set; }

    /// <summary>Requests in the current rolling minute.</summary>
    [JsonPropertyName("requestsLastMinute")]
    public int RequestsLastMinute { get; set; }

    /// <summary>Tokens in the current rolling minute.</summary>
    [JsonPropertyName("tokensLastMinute")]
    public long TokensLastMinute { get; set; }

    /// <summary>Tokens since UTC midnight.</summary>
    [JsonPropertyName("tokensToday")]
    public long TokensToday { get; set; }
}
