using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>Blocking response for <c>POST /api/chat</c>.</summary>
public sealed class ChatResponse
{
    /// <summary>Model the response came from.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>When the node produced the response.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Assistant message.</summary>
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    /// <summary>Whether generation completed (always <c>true</c> for blocking calls).</summary>
    [JsonPropertyName("done")]
    public bool? Done { get; set; }

    /// <summary>Total wall time on the node, in nanoseconds.</summary>
    [JsonPropertyName("total_duration")]
    public long? TotalDuration { get; set; }

    /// <summary>Model load time on the node, in nanoseconds.</summary>
    [JsonPropertyName("load_duration")]
    public long? LoadDuration { get; set; }

    /// <summary>Prompt token count.</summary>
    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; set; }

    /// <summary>Prompt evaluation duration, in nanoseconds.</summary>
    [JsonPropertyName("prompt_eval_duration")]
    public long? PromptEvalDuration { get; set; }

    /// <summary>Generated token count.</summary>
    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; set; }

    /// <summary>Generation duration, in nanoseconds.</summary>
    [JsonPropertyName("eval_duration")]
    public long? EvalDuration { get; set; }

    /// <summary>
    /// Terminal error surfaced by the coordinator. Populated when a streaming call ends
    /// with the <c>{ "error": …, "done": true }</c> contract; always <c>null</c> on success.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Any additional Ollama-shaped fields (e.g. <c>done_reason</c>).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    /// <summary>
    /// Ids of the records that grounded this response, parsed from the <c>X-InferHub-Sources</c>
    /// response header. Populated only for a call made with a <see cref="RetrievalOptions"/>;
    /// <c>null</c> when retrieval was not requested or the coordinator returned no sources header.
    /// Not part of the JSON body.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string>? SourceIds { get; set; }
}
