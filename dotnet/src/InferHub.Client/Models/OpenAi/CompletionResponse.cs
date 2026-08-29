using System.Text.Json.Serialization;

namespace InferHub.Client.Models.OpenAi;

/// <summary>
/// Response for <c>POST /v1/completions</c>. Legacy completions use <b>one</b> shape for both
/// blocking and streamed answers — unlike chat, there is no separate chunk object, so a streamed
/// call yields this same type once per frame.
/// </summary>
public sealed class CompletionResponse
{
    /// <summary>Completion id (<c>cmpl-…</c>).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Always <c>text_completion</c>.</summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>Creation time, in Unix seconds.</summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>Model that answered.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Answers. Empty on the final usage frame of a streamed call.</summary>
    [JsonPropertyName("choices")]
    public IReadOnlyList<CompletionChoice> Choices { get; set; } = Array.Empty<CompletionChoice>();

    /// <summary>Token counts, when the node reported them.</summary>
    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }

    /// <summary>
    /// Which node or provider answered, from <c>X-InferHub-Served-By</c>. Reported, never
    /// interpreted. Not part of the JSON body.
    /// </summary>
    [JsonIgnore]
    public string? ServedBy { get; set; }
}

/// <summary>One answer inside a <see cref="CompletionResponse"/>.</summary>
public sealed class CompletionChoice
{
    /// <summary>Choice index.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>Generated text. On a streamed call this is the increment, not the whole answer.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary><c>stop</c> or <c>length</c>; set on the terminal frame.</summary>
    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}
