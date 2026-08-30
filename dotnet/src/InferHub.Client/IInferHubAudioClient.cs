using InferHub.Client.Models.Audio;

namespace InferHub.Client;

/// <summary>
/// Client for the hub's audio surface — <c>POST /v1/audio/transcriptions</c> and
/// <c>POST /v1/audio/speech</c>. Same hub, same base address, same client API key as
/// <see cref="IInferHubClient"/> and <see cref="IInferHubOpenAiClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// A third interface rather than four more methods on <see cref="IInferHubOpenAiClient"/>: that one
/// is published, and a new member on a published interface breaks every caller holding a test
/// double or a decorator. One interface per published surface, and a published interface never
/// grows.
/// </para>
/// <para>
/// <b>There is no <see cref="InferHubCallOptions"/> overload here, deliberately.</b> These two
/// routes read neither <c>X-InferHub-Provider</c> nor <c>X-InferHub-Conversation</c> nor the
/// retrieval headers: audio is dispatched to a node that declared the capability, and no cloud
/// provider is in the path at all. An overload would compile, send a header nothing reads, and be a
/// documented feature that does not work.
/// </para>
/// <para>
/// Failures arrive in the OpenAI envelope and surface as
/// <see cref="Exceptions.InferHubOpenAiException"/>. Two are worth catching by status: a
/// <c>503</c> with code <c>capability_unavailable</c> means the fleet has the model but no node
/// currently provides <c>transcribe</c>/<c>speak</c> — it carries <c>Retry-After</c> and is not a
/// <c>404</c>; a <c>404</c> means no node holds the model at all.
/// </para>
/// </remarks>
public interface IInferHubAudioClient
{
    /// <summary>
    /// Transcribe audio and parse the result — <c>POST /v1/audio/transcriptions</c>.
    /// </summary>
    /// <remarks>
    /// Always asks the hub for <c>verbose_json</c>, whatever
    /// <see cref="TranscriptionRequest.ResponseFormat"/> says, because that is the shape carrying
    /// language, duration and segments. For <c>text</c>, <c>srt</c> or <c>vtt</c> use
    /// <see cref="TranscribeDocumentAsync"/>, which returns the hub's own bytes untouched.
    /// </remarks>
    /// <param name="request">The audio and the model. <see cref="TranscriptionRequest.Audio"/> is required and is not disposed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Transcription> TranscribeAsync(TranscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribe audio into the format <see cref="TranscriptionRequest.ResponseFormat"/> asks for,
    /// and return it verbatim — <c>text</c>, <c>srt</c> or <c>vtt</c>.
    /// </summary>
    /// <remarks>
    /// The subtitle formats are rendered by the hub from the worker's segments, so a worker that
    /// answered with text alone produces an empty one. That is the hub's answer and this method does
    /// not improve on it.
    /// </remarks>
    /// <param name="request">The audio, the model, and the <c>response_format</c> to render.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TranscriptionDocument> TranscribeDocumentAsync(TranscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synthesise speech — <c>POST /v1/audio/speech</c> — and hand back the response stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole file when <see cref="SpeechRequest.StreamFormat"/> is null, and the file
    /// as it is made when it is <see cref="SpeechStreamFormats.Audio"/>. <b>The caller's code is
    /// identical either way</b>, because this library hands over the live stream rather than
    /// buffering somebody's audio to be friendly.
    /// </para>
    /// <para>
    /// <b>Dispose the result</b> — it holds the HTTP response. No per-call timeout is applied: a
    /// synthesis is long by nature, so the caller's <paramref name="cancellationToken"/> is the one
    /// that governs it.
    /// </para>
    /// </remarks>
    /// <param name="request">Model, text, voice, container and optional <c>stream_format</c>.</param>
    /// <param name="cancellationToken">Cancels the request, and the read while the caller holds the stream.</param>
    Task<SpeechAudio> CreateSpeechAsync(SpeechRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synthesise speech as server-sent events — <c>stream_format: "sse"</c> — yielding one chunk
    /// per <c>speech.audio.delta</c> and then the terminal <c>speech.audio.done</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The terminal frame is yielded like any other, carrying <see cref="SpeechChunk.Usage"/> and no
    /// audio; <b>a usage of three zeros is a true count</b>, and the number that reconciles with a
    /// bill is <see cref="SpeechChunk.Characters"/>. Every chunk also carries
    /// <see cref="SpeechChunk.ServedBy"/> and <see cref="SpeechChunk.SampleRate"/>, read once from
    /// the response headers before the first frame.
    /// </para>
    /// <para>
    /// A <c>speech.audio.error</c> frame — the hub's extension for a stream that died after the
    /// caller already held a <c>200</c> — is raised as
    /// <see cref="Exceptions.InferHubOpenAiException"/>. A partial answer plus a clean exception is
    /// the contract, and nothing is ever retried.
    /// </para>
    /// <para>
    /// <see cref="SpeechRequest.StreamFormat"/> is forced to <see cref="SpeechStreamFormats.Sse"/>.
    /// Only <c>wav</c> and <c>pcm</c> can be streamed; anything else is refused by the hub with a
    /// <c>400</c> before a node is chosen, so nothing is spent.
    /// </para>
    /// </remarks>
    /// <param name="request">Model, text, voice and container.</param>
    /// <param name="cancellationToken">Cancels the read loop.</param>
    IAsyncEnumerable<SpeechChunk> StreamSpeechAsync(SpeechRequest request, CancellationToken cancellationToken = default);
}
