using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.OpenAi;

/// <summary>
/// Request body for the legacy <c>POST /v1/completions</c>. Maps to the Ollama <c>generate</c>
/// job kind. Prefer <see cref="ChatCompletionRequest"/> for new code; this exists for callers
/// whose prompts were written against the older endpoint.
/// </summary>
public sealed class CompletionRequest
{
    /// <summary>Model name. Required.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// The prompt. Only the single-string form is served — the hub rejects token arrays rather
    /// than guessing a tokenizer. Use <see cref="FromText"/> for the common case.
    /// </summary>
    [JsonPropertyName("prompt")]
    public JsonElement? Prompt { get; set; }

    /// <summary>Set by the client; whatever you set here is overwritten.</summary>
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

    /// <summary>Maximum tokens to generate.</summary>
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    /// <summary>Stop sequences — a string or an array of strings.</summary>
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

    /// <summary>Any other OpenAI-shaped field.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    /// <summary>A request for one model and one prompt string.</summary>
    /// <param name="model">Model name.</param>
    /// <param name="prompt">Prompt text.</param>
    public static CompletionRequest FromText(string model, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(prompt);
        return new CompletionRequest
        {
            Model = model,
            Prompt = JsonSerializer.SerializeToElement(prompt, Serialization.InferHubJsonContext.Default.String)
        };
    }
}
