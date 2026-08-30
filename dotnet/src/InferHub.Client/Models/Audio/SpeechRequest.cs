using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Audio;

/// <summary>
/// A <c>POST /v1/audio/speech</c> request, in OpenAI's own shape.
/// </summary>
public sealed class SpeechRequest
{
    /// <summary>The synthesis model, e.g. <c>piper</c>. Required.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The text to speak. Required and never empty — <c>" "</c> is legitimate (a pause) and the hub
    /// accepts it. This is content: the library never logs it, and the hub meters its
    /// <b>character count</b> rather than tokens.
    /// </summary>
    [JsonPropertyName("input")]
    public string Input { get; set; } = string.Empty;

    /// <summary>The voice, as the fleet's worker names it. Null takes the worker's default.</summary>
    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    /// <summary>
    /// The container — one of <see cref="SpeechFormats"/>. Defaults to <c>wav</c> at the hub when
    /// omitted. A format the worker cannot produce is refused, never substituted.
    /// </summary>
    [JsonPropertyName("response_format")]
    public string? ResponseFormat { get; set; }

    /// <summary>Playback rate between <c>0.25</c> and <c>4.0</c>. Outside that the hub answers <c>400</c>.</summary>
    [JsonPropertyName("speed")]
    public double? Speed { get; set; }

    /// <summary>
    /// One of <see cref="SpeechStreamFormats"/>, or null for the whole file at once. Set by
    /// <see cref="IInferHubAudioClient.StreamSpeechAsync"/> to <c>sse</c>; leave it null or set it
    /// to <c>audio</c> for <see cref="IInferHubAudioClient.CreateSpeechAsync"/>, which reads both
    /// the same way.
    /// </summary>
    [JsonPropertyName("stream_format")]
    public string? StreamFormat { get; set; }

    /// <summary>Shorthand for the common call.</summary>
    /// <param name="model">Synthesis model.</param>
    /// <param name="input">The text to speak.</param>
    /// <param name="voice">Optional voice.</param>
    /// <param name="responseFormat">Optional container — one of <see cref="SpeechFormats"/>.</param>
    public static SpeechRequest Create(string model, string input, string? voice = null, string? responseFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrEmpty(input);

        return new SpeechRequest
        {
            Model = model,
            Input = input,
            Voice = voice,
            ResponseFormat = responseFormat
        };
    }
}
