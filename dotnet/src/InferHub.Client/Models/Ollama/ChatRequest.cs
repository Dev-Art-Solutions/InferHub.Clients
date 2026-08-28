using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>Body for <c>POST /api/chat</c>.</summary>
public sealed class ChatRequest
{
    /// <summary>Model tag (required — the coordinator returns 400 otherwise).</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Conversation history. The coordinator does not retain content — send the full history each turn.</summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<ChatMessage>? Messages { get; set; }

    /// <summary>Whether to stream NDJSON chunks. <see cref="IInferHubClient.ChatAsync(ChatRequest, System.Threading.CancellationToken)"/> forces this to <c>false</c>.</summary>
    [JsonPropertyName("stream")]
    public bool? Stream { get; set; }

    /// <summary>Raw Ollama <c>options</c> block (temperature, num_ctx, etc.). Passed through untouched.</summary>
    [JsonPropertyName("options")]
    public JsonElement? Options { get; set; }

    /// <summary>Any additional Ollama-shaped fields (tools, format, keep_alive, …).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
