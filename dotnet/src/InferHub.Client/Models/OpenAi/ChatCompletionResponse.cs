using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.OpenAi;

/// <summary>Blocking response for <c>POST /v1/chat/completions</c>.</summary>
public sealed class ChatCompletionResponse
{
    /// <summary>Completion id (<c>chatcmpl-…</c>), minted by the hub.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Always <c>chat.completion</c>.</summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>Creation time, in Unix seconds.</summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>Model that answered.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Answers. The hub returns exactly one.</summary>
    [JsonPropertyName("choices")]
    public IReadOnlyList<ChatCompletionChoice> Choices { get; set; } = Array.Empty<ChatCompletionChoice>();

    /// <summary>Token counts, when the node reported them. Absent rather than zeroed when it did not.</summary>
    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }

    /// <summary>
    /// Which node or provider answered, from the <c>X-InferHub-Served-By</c> response header —
    /// a node id, or <c>provider:&lt;id&gt;</c> for a vendor-served answer. <c>null</c> when the hub
    /// sent no header. Reported, never interpreted: this library never routes or retries on it.
    /// Not part of the JSON body.
    /// </summary>
    [JsonIgnore]
    public string? ServedBy { get; set; }

    /// <summary>
    /// Ids of the records that grounded this response, from <c>X-InferHub-Sources</c>. Populated
    /// only when the call asked for retrieval. Not part of the JSON body.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string>? SourceIds { get; set; }
}

/// <summary>One answer inside a <see cref="ChatCompletionResponse"/>.</summary>
public sealed class ChatCompletionChoice
{
    /// <summary>Position in <see cref="ChatCompletionResponse.Choices"/>.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>The assistant message.</summary>
    [JsonPropertyName("message")]
    public ChatCompletionResponseMessage? Message { get; set; }

    /// <summary><c>stop</c>, <c>length</c> or <c>tool_calls</c>.</summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>The assistant message inside a <see cref="ChatCompletionChoice"/>.</summary>
public sealed class ChatCompletionResponseMessage
{
    /// <summary>Always <c>assistant</c>.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Generated text. Empty (not null) when the model answered with tool calls alone.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Tool calls the model asked for. Note that <c>function.arguments</c> is a JSON
    /// <em>string</em> on the wire, not an object — that is the dialect, not a hub quirk.
    /// </summary>
    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<OpenAiToolCall>? ToolCalls { get; set; }

    /// <summary>Any additional fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>A tool call requested by the model.</summary>
public sealed class OpenAiToolCall
{
    /// <summary>Call id, echoed back on the matching <c>tool</c> message.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Always <c>function</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Position within a streamed <c>delta.tool_calls</c> array; absent on blocking answers.</summary>
    [JsonPropertyName("index")]
    public int? Index { get; set; }

    /// <summary>The function and its arguments.</summary>
    [JsonPropertyName("function")]
    public OpenAiToolCallFunction? Function { get; set; }
}

/// <summary>The function half of an <see cref="OpenAiToolCall"/>.</summary>
public sealed class OpenAiToolCallFunction
{
    /// <summary>Function name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Arguments as a JSON <em>string</em> — parse it yourself, the dialect sends it encoded.</summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

/// <summary>
/// Token counts. Absent when the node reported none; a zero here is a measured zero, not a
/// placeholder (the hub is careful about this in both directions).
/// </summary>
public sealed class OpenAiUsage
{
    /// <summary>Prompt tokens.</summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    /// <summary>Generated tokens.</summary>
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    /// <summary>Sum of the two.</summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
