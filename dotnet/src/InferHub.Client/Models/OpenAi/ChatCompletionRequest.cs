using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.OpenAi;

/// <summary>
/// Request body for <c>POST /v1/chat/completions</c> — the hub's OpenAI-compatible dialect.
/// The hub translates it into an Ollama chat job and translates the answer back, so the same
/// models, the same fleet and the same retrieval headers apply.
/// </summary>
public sealed class ChatCompletionRequest
{
    /// <summary>Model name, as advertised by <c>/v1/models</c>. Required.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>The conversation so far. Full history is re-sent every turn; the client stores none of it.</summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<ChatCompletionMessage>? Messages { get; set; }

    /// <summary>
    /// Set by the client: <c>false</c> for <c>CreateChatCompletionAsync</c>, <c>true</c> for
    /// <c>StreamChatCompletionAsync</c>. Whatever you set here is overwritten.
    /// </summary>
    [JsonPropertyName("stream")]
    public bool? Stream { get; set; }

    /// <summary>Streaming extras — set <c>IncludeUsage</c> to get a final usage-only frame.</summary>
    [JsonPropertyName("stream_options")]
    public ChatCompletionStreamOptions? StreamOptions { get; set; }

    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>Nucleus sampling cutoff.</summary>
    [JsonPropertyName("top_p")]
    public double? TopP { get; set; }

    /// <summary>Maximum tokens to generate. Superseded by <see cref="MaxCompletionTokens"/> when both are sent.</summary>
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    /// <summary>Maximum tokens to generate; the newer spelling, which wins over <see cref="MaxTokens"/>.</summary>
    [JsonPropertyName("max_completion_tokens")]
    public int? MaxCompletionTokens { get; set; }

    /// <summary>Stop sequences — a string or an array of strings. Raw JSON, passed through.</summary>
    [JsonPropertyName("stop")]
    public JsonElement? Stop { get; set; }

    /// <summary>Presence penalty.</summary>
    [JsonPropertyName("presence_penalty")]
    public double? PresencePenalty { get; set; }

    /// <summary>Frequency penalty.</summary>
    [JsonPropertyName("frequency_penalty")]
    public double? FrequencyPenalty { get; set; }

    /// <summary>Sampling seed, where the node's runtime honours one.</summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>Response format (<c>{"type":"json_object"}</c> and friends). Raw JSON, passed through.</summary>
    [JsonPropertyName("response_format")]
    public JsonElement? ResponseFormat { get; set; }

    /// <summary>
    /// Tool definitions, passed through as raw JSON. Deliberately untyped: the hub does not
    /// interpret them either, and typing an evolving vendor schema in a 1.x package is a
    /// maintenance treadmill on somebody else's release cadence.
    /// </summary>
    [JsonPropertyName("tools")]
    public JsonElement? Tools { get; set; }

    /// <summary>Tool-choice directive, passed through as raw JSON.</summary>
    [JsonPropertyName("tool_choice")]
    public JsonElement? ToolChoice { get; set; }

    /// <summary>Any other OpenAI-shaped field you want to send. The hub logs what it ignores.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

/// <summary>Streaming extras for <see cref="ChatCompletionRequest"/> and <see cref="CompletionRequest"/>.</summary>
public sealed class ChatCompletionStreamOptions
{
    /// <summary>
    /// Ask for a final frame carrying <c>usage</c>. That frame has an empty <c>choices</c> array
    /// and is yielded like any other — it is where the token counts arrive.
    /// </summary>
    [JsonPropertyName("include_usage")]
    public bool? IncludeUsage { get; set; }
}

/// <summary>One message in an OpenAI-dialect chat exchange.</summary>
public sealed class ChatCompletionMessage
{
    /// <summary>Role — <c>system</c>, <c>user</c>, <c>assistant</c> or <c>tool</c>.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>
    /// Message content: a plain string, or an array of content parts. Raw JSON, because both
    /// shapes are legal on the wire and the hub accepts both.
    /// </summary>
    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }

    /// <summary>Optional speaker name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Tool calls this assistant message is replaying. Raw JSON, passed through.</summary>
    [JsonPropertyName("tool_calls")]
    public JsonElement? ToolCalls { get; set; }

    /// <summary>The tool call this <c>tool</c> message answers.</summary>
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    /// <summary>Any additional OpenAI-shaped fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    /// <summary>A <c>system</c> message with plain text content.</summary>
    /// <param name="content">Message text.</param>
    public static ChatCompletionMessage System(string content) => Text("system", content);

    /// <summary>A <c>user</c> message with plain text content.</summary>
    /// <param name="content">Message text.</param>
    public static ChatCompletionMessage User(string content) => Text("user", content);

    /// <summary>An <c>assistant</c> message with plain text content.</summary>
    /// <param name="content">Message text.</param>
    public static ChatCompletionMessage Assistant(string content) => Text("assistant", content);

    private static ChatCompletionMessage Text(string role, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new ChatCompletionMessage
        {
            Role = role,
            Content = JsonSerializer.SerializeToElement(content, Serialization.InferHubJsonContext.Default.String)
        };
    }
}
