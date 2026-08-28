using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>Body for <c>POST /api/generate</c>.</summary>
public sealed class GenerateRequest
{
    /// <summary>Model tag (required).</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Prompt text.</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    /// <summary>Whether to stream NDJSON chunks. <see cref="IInferHubClient.GenerateAsync(GenerateRequest, System.Threading.CancellationToken)"/> forces this to <c>false</c>.</summary>
    [JsonPropertyName("stream")]
    public bool? Stream { get; set; }

    /// <summary>Raw Ollama <c>options</c> block. Passed through untouched.</summary>
    [JsonPropertyName("options")]
    public JsonElement? Options { get; set; }

    /// <summary>Any additional Ollama-shaped fields (system, template, context, format, …).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
