using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>
/// Body for the legacy <c>POST /api/embeddings</c> endpoint. Kept for drop-in callers
/// that have not migrated to the batch-capable <c>/api/embed</c>.
/// </summary>
public sealed class EmbeddingsRequest
{
    /// <summary>Embedding model tag (required).</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Single input string to embed.</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    /// <summary>Ollama <c>keep_alive</c> hint passed through to the node.</summary>
    [JsonPropertyName("keep_alive")]
    public string? KeepAlive { get; set; }
}
