using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Audio;

/// <summary>
/// A parsed transcript — the <c>verbose_json</c> shape of
/// <c>POST /v1/audio/transcriptions</c>.
/// </summary>
/// <remarks>
/// <see cref="Segments"/> comes free from a Whisper-shaped worker and is empty from one that
/// answers with text alone. It is empty rather than null for the same reason the hub renders an
/// empty subtitle file rather than a fabricated one: an absent segment list is a fact about the
/// worker, not a failure to report.
/// </remarks>
public sealed class Transcription
{
    /// <summary>Always <c>transcribe</c>.</summary>
    [JsonPropertyName("task")]
    public string? Task { get; set; }

    /// <summary>The language the worker detected or was told, when it reports one.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>Length of the audio in seconds, when the worker measured it.</summary>
    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    /// <summary>The transcript.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Timed segments. Empty when the worker returned text alone.</summary>
    [JsonPropertyName("segments")]
    public IReadOnlyList<TranscriptionSegment> Segments { get; set; } = Array.Empty<TranscriptionSegment>();

    /// <summary>
    /// Which node answered, from <c>X-InferHub-Served-By</c>. <c>null</c> when the hub sent no
    /// header. Reported, never interpreted. Not part of the JSON body.
    /// </summary>
    [JsonIgnore]
    public string? ServedBy { get; set; }
}

/// <summary>One timed line of a <see cref="Transcription"/>.</summary>
public sealed class TranscriptionSegment
{
    /// <summary>Position in the transcript, from the worker or filled in by its index.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Start, in seconds from the beginning of the audio.</summary>
    [JsonPropertyName("start")]
    public double Start { get; set; }

    /// <summary>End, in seconds from the beginning of the audio.</summary>
    [JsonPropertyName("end")]
    public double End { get; set; }

    /// <summary>The text of this segment.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// A transcription rendered by the hub into a format this library does not parse —
/// <c>text</c>, <c>srt</c> or <c>vtt</c>.
/// </summary>
/// <remarks>
/// <see cref="Content"/> is the hub's own bytes, decoded as UTF-8 and otherwise untouched. A
/// subtitle file is a file: reinterpreting it into a transcript object would lose the cue timings
/// that were the reason to ask for it.
/// </remarks>
/// <param name="Format">The <c>response_format</c> that produced it.</param>
/// <param name="ContentType">The hub's own content type — <c>text/vtt</c> for VTT, <c>text/plain</c> for the rest.</param>
/// <param name="Content">The rendered document, verbatim.</param>
public sealed record TranscriptionDocument(string Format, string ContentType, string Content)
{
    /// <summary>
    /// Which node answered, from <c>X-InferHub-Served-By</c>. <c>null</c> when the hub sent no
    /// header. Reported, never interpreted.
    /// </summary>
    public string? ServedBy { get; init; }
}
