namespace InferHub.Client.Models.Audio;

/// <summary>
/// The <c>response_format</c> values <c>POST /v1/audio/transcriptions</c> accepts.
/// </summary>
/// <remarks>
/// Constants rather than an enum: the hub takes a string, refuses an unknown one with a
/// <c>400</c> that names the list, and may grow the list in a release this package has not
/// shipped for. An enum here would turn "your hub is newer than your client" into a value the
/// caller cannot express.
/// </remarks>
public static class TranscriptionFormats
{
    /// <summary>OpenAI's default — <c>{"text":"…"}</c> and nothing else.</summary>
    public const string Json = "json";

    /// <summary>The transcript as <c>text/plain</c>, with no envelope.</summary>
    public const string Text = "text";

    /// <summary>SubRip subtitles. Rendered at the edge from the worker's segments.</summary>
    public const string Srt = "srt";

    /// <summary>WebVTT subtitles. Rendered at the edge from the worker's segments.</summary>
    public const string Vtt = "vtt";

    /// <summary>Text, language, duration and segments — what <see cref="Transcription"/> is parsed from.</summary>
    public const string VerboseJson = "verbose_json";
}

/// <summary>
/// The audio containers <c>POST /v1/audio/speech</c> knows about.
/// </summary>
/// <remarks>
/// A format the fleet's worker cannot produce is <b>refused with a 400, never substituted</b>: a
/// caller who asked for mp3 and got a wav has a corrupted file with a confident content type, and
/// finds out in a media player three days later. Only <see cref="Wav"/> and <see cref="Pcm"/> are
/// native to the shipped TTS worker; the rest need an encoder in the worker's environment.
/// </remarks>
public static class SpeechFormats
{
    /// <summary>RIFF/WAVE. The default, and streamable.</summary>
    public const string Wav = "wav";

    /// <summary>MPEG audio. Needs an encoder on the node.</summary>
    public const string Mp3 = "mp3";

    /// <summary>Opus in an Ogg container. Needs an encoder on the node.</summary>
    public const string Opus = "opus";

    /// <summary>FLAC. Needs an encoder on the node.</summary>
    public const string Flac = "flac";

    /// <summary>
    /// Headerless 16-bit little-endian samples at the voice's own rate. Streamable, and the only
    /// way to learn the rate is <see cref="SpeechAudio.SampleRate"/> — which is why the hub sends
    /// it on a header and this client surfaces it.
    /// </summary>
    public const string Pcm = "pcm";
}

/// <summary>
/// OpenAI's <c>stream_format</c> on <c>POST /v1/audio/speech</c>. <b>Absent is not a third
/// value</b> — it is the whole file at once, byte for byte what the hub answered before it learned
/// to stream.
/// </summary>
/// <remarks>
/// Only <see cref="SpeechFormats.Wav"/> and <see cref="SpeechFormats.Pcm"/> can be streamed —
/// concatenability is the whole contract, and a chunk boundary is not a codec frame boundary.
/// Asking to stream anything else is a <c>400</c> from the hub before a node is chosen, so nothing
/// is spent or synthesised first.
/// </remarks>
public static class SpeechStreamFormats
{
    /// <summary>
    /// Framed as server-sent events — <c>speech.audio.delta</c>, then <c>speech.audio.done</c>.
    /// What <see cref="IInferHubAudioClient.StreamSpeechAsync"/> asks for.
    /// </summary>
    public const string Sse = "sse";

    /// <summary>
    /// The raw container on a chunked body, written as it is made. Read it with
    /// <see cref="IInferHubAudioClient.CreateSpeechAsync"/>, which is the same method that reads
    /// the buffered answer — this client never held the whole file either way.
    /// </summary>
    public const string Audio = "audio";
}
