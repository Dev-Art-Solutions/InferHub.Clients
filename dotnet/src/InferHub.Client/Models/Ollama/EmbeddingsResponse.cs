using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>Response for the legacy <c>POST /api/embeddings</c> endpoint.</summary>
public sealed class EmbeddingsResponse
{
    /// <summary>Single vector produced by the model.</summary>
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
