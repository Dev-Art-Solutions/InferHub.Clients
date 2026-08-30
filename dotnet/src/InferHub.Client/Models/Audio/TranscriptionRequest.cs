namespace InferHub.Client.Models.Audio;

/// <summary>
/// A <c>POST /v1/audio/transcriptions</c> request: one audio stream, the model to transcribe it
/// with, and the optional hints OpenAI's API defines.
/// </summary>
/// <remarks>
/// <para>
/// The client <b>does not own</b> <see cref="Audio"/> and never disposes it — open the file, make
/// the call, dispose it yourself. It is a <see cref="Stream"/> rather than a <c>byte[]</c> so a
/// 200 MB recording is copied to the socket rather than held twice in memory.
/// </para>
/// <para>
/// The form is written with every field <b>before</b> the file part. Above the hub's
/// <c>Tools:MaxStreamedBytes</c> the request is routed from the leading fields while the bytes are
/// still arriving, so a field after the file is a <c>400</c> — and a <c>model</c> the hub never saw
/// would be a transcription answered by the wrong node.
/// </para>
/// </remarks>
public sealed class TranscriptionRequest
{
    /// <summary>The transcription model, e.g. <c>whisper-1</c>. Required.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The audio. Required, read from its current position to the end, and never disposed by this
    /// library.
    /// </summary>
    public Stream? Audio { get; set; }

    /// <summary>
    /// File name for the <c>file</c> part. Some workers infer the container from its extension, so
    /// pass a real one. It is sent to the hub and, per the hub's own rule, never logged there.
    /// </summary>
    public string FileName { get; set; } = "audio.wav";

    /// <summary>Media type of the <c>file</c> part. Defaults to <c>application/octet-stream</c>.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>ISO-639-1 hint, e.g. <c>bg</c>. Null lets the model detect it.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// A hint at spelling or style — proper nouns, jargon. It is a prompt, so it is content: this
    /// library never logs it.
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>Sampling temperature. Null leaves it to the worker.</summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// The shape the hub renders — one of <see cref="TranscriptionFormats"/>. Used only by
    /// <see cref="IInferHubAudioClient.TranscribeDocumentAsync"/>;
    /// <see cref="IInferHubAudioClient.TranscribeAsync"/> forces
    /// <see cref="TranscriptionFormats.VerboseJson"/>, because that is the shape it parses.
    /// </summary>
    public string ResponseFormat { get; set; } = TranscriptionFormats.Json;

    /// <summary>Shorthand for a transcription of an already-open stream.</summary>
    /// <param name="model">Transcription model.</param>
    /// <param name="audio">The audio, not disposed by this library.</param>
    /// <param name="fileName">File name for the <c>file</c> part.</param>
    /// <param name="contentType">Media type of the <c>file</c> part.</param>
    public static TranscriptionRequest FromStream(
        string model,
        Stream audio,
        string fileName = "audio.wav",
        string contentType = "application/octet-stream")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(audio);

        return new TranscriptionRequest
        {
            Model = model,
            Audio = audio,
            FileName = fileName,
            ContentType = contentType
        };
    }

    /// <summary>Shorthand for audio already in memory.</summary>
    /// <param name="model">Transcription model.</param>
    /// <param name="audio">The audio bytes.</param>
    /// <param name="fileName">File name for the <c>file</c> part.</param>
    /// <param name="contentType">Media type of the <c>file</c> part.</param>
    public static TranscriptionRequest FromBytes(
        string model,
        byte[] audio,
        string fileName = "audio.wav",
        string contentType = "application/octet-stream")
    {
        ArgumentNullException.ThrowIfNull(audio);
        return FromStream(model, new MemoryStream(audio, writable: false), fileName, contentType);
    }
}
