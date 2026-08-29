using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.OpenAi;

/// <summary>
/// One SSE frame of a streamed <c>POST /v1/chat/completions</c>. The opening frame carries
/// <c>delta.role</c>, later frames carry <c>delta.content</c>, and the terminal frame carries an
/// empty delta with a <see cref="ChatCompletionChunkChoice.FinishReason"/>.
/// </summary>
/// <remarks>
/// A frame with an <b>empty</b> <see cref="Choices"/> list is the usage frame requested by
/// <c>stream_options.include_usage</c>. It is yielded rather than skipped: it is the only place
/// the token counts appear on a streamed call.
/// </remarks>
public sealed class ChatCompletionChunk
{
    /// <summary>Completion id — the same value on every frame of one answer.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Always <c>chat.completion.chunk</c>.</summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>Creation time, in Unix seconds.</summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>Model that answered.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>The deltas. Empty on the usage frame.</summary>
    [JsonPropertyName("choices")]
    public IReadOnlyList<ChatCompletionChunkChoice> Choices { get; set; } = Array.Empty<ChatCompletionChunkChoice>();

    /// <summary>Token counts; present only on the final usage frame.</summary>
    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }

    /// <summary>
    /// Which node or provider answered, from <c>X-InferHub-Served-By</c>. Read once before the
    /// first frame and repeated on every frame of the same stream. Not part of the JSON body.
    /// </summary>
    [JsonIgnore]
    public string? ServedBy { get; set; }

    /// <summary>Grounding record ids from <c>X-InferHub-Sources</c>, repeated on every frame. Not part of the JSON body.</summary>
    [JsonIgnore]
    public IReadOnlyList<string>? SourceIds { get; set; }
}

/// <summary>One delta inside a <see cref="ChatCompletionChunk"/>.</summary>
public sealed class ChatCompletionChunkChoice
{
    /// <summary>Choice index.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>What this frame adds.</summary>
    [JsonPropertyName("delta")]
    public ChatCompletionDelta? Delta { get; set; }

    /// <summary>Set on the terminal frame only.</summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>The incremental part of a streamed answer.</summary>
public sealed class ChatCompletionDelta
{
    /// <summary>Present on the opening frame only.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>The text this frame adds. Concatenate across frames.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Tool calls. The hub emits the whole call in one frame rather than fabricating
    /// argument-fragment streaming that the node never sent.
    /// </summary>
    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<OpenAiToolCall>? ToolCalls { get; set; }

    /// <summary>Any additional fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
