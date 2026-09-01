using InferHub.Client.Models.Images;
using InferHub.Client.Models.Videos;

namespace InferHub.Client;

/// <summary>
/// Client for the hub's video surface: OpenAI's own Videos API (<c>POST /v1/videos</c>,
/// <c>GET /v1/videos/{id}</c>, <c>/content</c>, <c>DELETE</c>) and the one route that dialect lacks
/// — <c>GET /api/videos/jobs</c>. Same hub, same base address, same client API key as
/// <see cref="IInferHubClient"/>, <see cref="IInferHubOpenAiClient"/>,
/// <see cref="IInferHubAudioClient"/> and <see cref="IInferHubImagesClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// A fifth interface rather than more methods on <see cref="IInferHubImagesClient"/>, even though
/// the hub runs both modalities through one job registry: that interface shipped in 1.3.0, and a new
/// member on a published interface breaks every caller holding a test double or a decorator. What is
/// shared is shared in <em>types</em> — <see cref="MediaJob"/> for the listing, the read-once stream,
/// the refusal to retry the request that destroys the bytes.
/// </para>
/// <para>
/// <b>Everything here is asynchronous, because the dialect is.</b> There is no "submit and wait"
/// twin the way images have one: <see cref="CreateAsync"/> answers with a queued
/// <see cref="Video"/>, and the render happens afterwards.
/// </para>
/// <para>
/// <b>Two routes of OpenAI's Videos API are refused by this hub and are therefore not methods
/// here.</b> <c>GET /v1/videos</c> (listing) and <c>POST /v1/videos/{id}/remix</c> both answer
/// <c>501</c> with <c>code == "not_supported"</c> and a sentence saying why — a video id is itself
/// the capability to fetch the bytes, and nothing durable holds the prompt that made a clip. A
/// method that could only throw would read as "this client has not got to it yet", which is the
/// opposite of true. To remix, send a new request with the prompt you want; to enumerate, call
/// <see cref="ListJobsAsync"/>.
/// </para>
/// <para>
/// Failures arrive in the OpenAI envelope and surface as
/// <see cref="Exceptions.InferHubOpenAiException"/> with <c>ErrorCode</c> and <c>Param</c> intact —
/// see <see cref="VideoErrorCodes"/>. Two carry a <c>Retry-After</c> that
/// <see cref="Exceptions.InferHubException.RetryAfter"/> surfaces:
/// <see cref="VideoErrorCodes.CapabilityUnavailable"/> and <see cref="VideoErrorCodes.QueueFull"/>.
/// </para>
/// </remarks>
public interface IInferHubVideoClient
{
    /// <summary>
    /// Ask for a clip — <c>POST /v1/videos</c>. Returns as soon as the hub has accepted it, with
    /// <c>status: queued</c> and the id that is the capability to fetch the bytes later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Keep the id.</b> There is no listing on this dialect to find it again with, and the
    /// job listing that does exist is a different route with a different id spelling
    /// (<see cref="VideoIdentifier"/>).
    /// </para>
    /// <para>
    /// The refusals worth catching by code: <c>404 model_not_found</c> (no such model in the fleet),
    /// <c>503 capability_unavailable</c> with <c>Retry-After</c> (the fleet holds the model and no
    /// node is currently rendering video — a different condition from the 404, and the only one of
    /// the two worth retrying), <c>503 queue_full</c>, and a <c>400</c> naming <c>size</c>,
    /// <c>seconds</c> or one of the <see cref="VideoHeaders"/> in <c>Param</c>.
    /// </para>
    /// </remarks>
    /// <param name="request">Model, prompt, size, duration and the <see cref="VideoOptions"/> knobs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Video> CreateAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// One clip — <c>GET /v1/videos/{id}</c>. <c>null</c> when there is no such clip.
    /// </summary>
    /// <remarks>
    /// A clip that is not yours, a malformed id and an <em>image</em> job's id are the same
    /// <c>404</c>, byte for byte, so <c>null</c> means one of those and cannot be used to learn
    /// which. <b>A failed render is not a null</b> — it is a document whose
    /// <see cref="Video.Status"/> is <c>failed</c> and whose <see cref="Video.Error"/> says why.
    /// </remarks>
    /// <param name="videoId">The <c>video_…</c> id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Video?> GetAsync(string videoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Watch a clip to its end by polling <c>GET /v1/videos/{id}</c>, yielding each document that
    /// says something new.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a poll, not a stream.</b> The image job seam has an SSE events route; the Videos
    /// dialect has none, and a video id on the images route is a <c>404</c> because those routes are
    /// scoped to the image capabilities. The loop is written here so that the thing a caller cannot
    /// guess is written once: <b><see cref="Video.Progress"/> is capped at 99 until the render is
    /// over</b>, so waiting for 100 is waiting one round trip past the answer. This enumeration ends
    /// on the terminal document.
    /// </para>
    /// <para>
    /// Cancelling the token stops the watch, not the render — walking away is not cancelling. Use
    /// <see cref="DeleteAsync"/> for that.
    /// </para>
    /// </remarks>
    /// <param name="videoId">The <c>video_…</c> id.</param>
    /// <param name="options">Poll interval and whether unchanged documents are yielded. <c>null</c> takes the defaults.</param>
    /// <param name="cancellationToken">Cancels the watch, not the render.</param>
    IAsyncEnumerable<Video> WatchAsync(string videoId, VideoWatchOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch the clip of a finished job — <c>GET /v1/videos/{id}/content</c>. <b>Reads it once.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The read unlinks the bytes at the hub. There is no second copy, no retry, and no way to ask
    /// for it again. Dispose the result — it holds the live response — and copy it somewhere durable
    /// before you do.
    /// </para>
    /// <para>
    /// Three refusals, three different fixes, all surfaced with their code intact:
    /// <see cref="VideoErrorCodes.NotReady"/> (<c>409</c> — it has not finished),
    /// <see cref="VideoErrorCodes.Expired"/> (<c>410</c> — it finished and the bytes are gone: read,
    /// evicted, or retention lapsed) and <see cref="VideoErrorCodes.NotFound"/> (<c>404</c>).
    /// </para>
    /// </remarks>
    /// <param name="videoId">The <c>video_…</c> id.</param>
    /// <param name="cancellationToken">Cancels the request, and the read while the caller holds the stream.</param>
    Task<VideoContent> OpenContentAsync(string videoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancel a render <b>and</b> discard its result — <c>DELETE /v1/videos/{id}</c>.
    /// </summary>
    /// <remarks>
    /// It is OpenAI's <c>delete</c> and it does both halves, which is not
    /// <see cref="IInferHubImagesClient.CancelJobAsync"/>'s bargain: there will be nothing to fetch
    /// afterwards either way. The cancel itself is best effort — a render stopped at step 27 of 28
    /// may still finish — but its output is dropped in the same operation.
    /// </remarks>
    /// <param name="videoId">The <c>video_…</c> id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<VideoDeletion> DeleteAsync(string videoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// This client's video jobs, oldest first, with the queue's own depth beside them —
    /// <c>GET /api/videos/jobs</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one route OpenAI's dialect lacks, in the hub's own job vocabulary: rows are
    /// <see cref="MediaJob"/>, with a bare GUID id and a <c>state</c> rather than a
    /// <c>video_…</c> id and a <c>status</c>. <see cref="VideoIdentifier.ToVideoId"/> converts one
    /// to the other.
    /// </para>
    /// <para>
    /// Client-scoped and capability-scoped: never a fleet-wide listing, and an image job is not in
    /// here. <b>It lists work, not results</b> — a clip that has been delivered is still here and has
    /// nothing left to fetch. The queue numbers beside the rows are fleet-wide, because there is one
    /// queue for both modalities.
    /// </para>
    /// <para>
    /// <b>A hub route.</b> A solo node serves the whole <c>/v1/videos</c> dialect and not this: it
    /// keeps no index of jobs to enumerate.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MediaJobList> ListJobsAsync(CancellationToken cancellationToken = default);
}
