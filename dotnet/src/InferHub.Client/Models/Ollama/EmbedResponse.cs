using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>Response for <c>POST /api/embed</c>.</summary>
public sealed class EmbedResponse
{
    /// <summary>Model that produced the vectors.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Embeddings, in the same order as the request's <c>input</c> array. Contains a single
    /// vector when the request's input was a plain string.
    /// </summary>
    [JsonPropertyName("embeddings")]
    public float[][] Embeddings { get; set; } = Array.Empty<float[]>();

    /// <summary>Total wall time on the node, in nanoseconds.</summary>
    [JsonPropertyName("total_duration")]
    public long? TotalDuration { get; set; }

    /// <summary>Model load time on the node, in nanoseconds.</summary>
    [JsonPropertyName("load_duration")]
    public long? LoadDuration { get; set; }

    /// <summary>Prompt token count.</summary>
    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; set; }

    /// <summary>Any additional Ollama-shaped fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
