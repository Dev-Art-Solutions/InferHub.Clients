using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>
/// A single message in a chat exchange. Roles used by the coordinator are
/// <c>system</c>, <c>user</c>, <c>assistant</c>, and <c>tool</c>.
/// </summary>
public sealed class ChatMessage
{
    /// <summary>Message role (<c>system</c>, <c>user</c>, <c>assistant</c>, <c>tool</c>).</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Message text content.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>Optional base64-encoded images (multimodal chats). Kept as raw JSON.</summary>
    [JsonPropertyName("images")]
    public JsonElement? Images { get; set; }

    /// <summary>Any additional Ollama-shaped fields the caller wants to pass through.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
