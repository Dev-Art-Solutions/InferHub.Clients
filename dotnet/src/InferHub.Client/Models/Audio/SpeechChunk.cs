using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Audio;

/// <summary>
/// One frame of a streamed synthesis — <c>speech.audio.delta</c> carrying audio, or the terminal
/// <c>speech.audio.done</c> carrying a count and no audio.
/// </summary>
/// <remarks>
/// The terminal frame is yielded rather than swallowed, exactly as the choice-less usage frame is
/// on <c>/v1/chat/completions</c>: a caller who learned that rule once has learned this one. Check
/// <see cref="Usage"/> — when it is non-null, <see cref="Audio"/> is empty and the stream is over.
/// A <c>speech.audio.error</c> frame is never yielded; it is thrown.
/// </remarks>
public sealed class SpeechChunk
{
    /// <summary>
    /// The frame's own <c>type</c> — <c>speech.audio.delta</c> or <c>speech.audio.done</c>. The
    /// same value the SSE <c>event:</c> line carries, which is why this client keys on the payload
    /// and needs no lookahead.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The audio, decoded from the frame's base64. Empty on the terminal frame. These are raw bytes
    /// of the container the request asked for: concatenating every chunk in order is the whole
    /// answer, which is why only <c>wav</c> and <c>pcm</c> may be streamed at all — the first chunk
    /// carries the header and the rest are samples.
    /// </summary>
    [JsonPropertyName("audio")]
    public byte[] Audio { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Token counts, on the terminal frame only, and <c>null</c> on every audio frame.
    /// <b>Three zeros here is a true count, not a placeholder</b> — a phoneme model tokenized
    /// nothing. The number that reconciles with a bill is <see cref="Characters"/>.
    /// </summary>
    [JsonPropertyName("usage")]
    public SpeechUsage? Usage { get; set; }

    /// <summary>
    /// Which node answered, from <c>X-InferHub-Served-By</c>, read once before the first frame and
    /// stamped on every chunk. Reported, never interpreted.
    /// </summary>
    [JsonIgnore]
    public string? ServedBy { get; set; }

    /// <summary>
    /// The sample rate the worker measured off its own first samples, from
    /// <c>X-InferHub-Audio-Sample-Rate</c>. Stamped on every chunk. For <c>pcm</c> this is the only
    /// place it exists.
    /// </summary>
    [JsonIgnore]
    public int? SampleRate { get; set; }

    /// <summary>
    /// What was metered, from <c>X-InferHub-Speech-Characters</c>: input characters, not tokens.
    /// Stamped on every chunk.
    /// </summary>
    [JsonIgnore]
    public long? Characters { get; set; }
}

/// <summary>
/// The usage block on <c>speech.audio.done</c>. OpenAI's schema requires the object, so the hub
/// emits it rather than omitting it — and for a phoneme model every field is legitimately zero.
/// </summary>
public sealed class SpeechUsage
{
    /// <summary>Input tokens. Zero from a model that does not tokenize, and that zero is measured.</summary>
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    /// <summary>Output tokens.</summary>
    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    /// <summary>Total tokens.</summary>
    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }
}
