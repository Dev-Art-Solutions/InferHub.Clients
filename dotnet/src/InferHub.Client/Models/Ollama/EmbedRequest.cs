using System.Text.Json;
using System.Text.Json.Serialization;
using InferHub.Client.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>
/// Body for <c>POST /api/embed</c> — the modern batch embeddings endpoint.
/// <see cref="Input"/> is either a JSON string (single) or a JSON array of strings (batch).
/// Use <see cref="FromText(string, string)"/> or <see cref="FromTexts(string, IEnumerable{string})"/>
/// if you don't want to build the <see cref="JsonElement"/> yourself.
/// </summary>
public sealed class EmbedRequest
{
    /// <summary>Embedding model tag (required — the coordinator returns 400 otherwise).</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Input to embed. String or string[] — see the factory helpers.</summary>
    [JsonPropertyName("input")]
    public JsonElement Input { get; set; }

    /// <summary>Whether to truncate inputs longer than the model's context window.</summary>
    [JsonPropertyName("truncate")]
    public bool? Truncate { get; set; }

    /// <summary>Ollama <c>keep_alive</c> hint passed through to the node.</summary>
    [JsonPropertyName("keep_alive")]
    public string? KeepAlive { get; set; }

    /// <summary>Any additional Ollama-shaped fields (options, etc.).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    /// <summary>Build a request that embeds a single string.</summary>
    public static EmbedRequest FromText(string model, string text)
    {
        return new EmbedRequest
        {
            Model = model,
            Input = JsonSerializer.SerializeToElement(text, InferHubJsonContext.Default.String)
        };
    }

    /// <summary>Build a request that embeds a batch of strings.</summary>
    public static EmbedRequest FromTexts(string model, IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return new EmbedRequest
        {
            Model = model,
            Input = JsonSerializer.SerializeToElement(texts as string[] ?? texts.ToArray(), InferHubJsonContext.Default.StringArray)
        };
    }
}
