using InferHub.Client.Models.Images;

namespace InferHub.Client;

/// <summary>
/// Client for the hub's image surface: the synchronous OpenAI routes
/// (<c>/v1/images/generations|edits|variations</c>) and the asynchronous job seam
/// (<c>/api/images/jobs</c>). Same hub, same base address, same client API key as
/// <see cref="IInferHubClient"/>, <see cref="IInferHubOpenAiClient"/> and
/// <see cref="IInferHubAudioClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// A fourth interface rather than more methods on <see cref="IInferHubOpenAiClient"/>: that one is
/// published, and a new member on a published interface breaks every caller holding a test double
/// or a decorator. One interface per published surface, and a published interface never grows.
/// </para>
/// <para>
/// <b>Synchronous and asynchronous are the same request; what differs is whether you wait.</b>
/// <see cref="GenerateAsync(ImageGenerationRequest, CancellationToken)"/> queues the job like any
/// other and holds the connection until it finishes — past the hub's <c>Images:SyncMaxWaitSeconds</c>
/// it answers <c>503</c> with code <c>job_still_running</c>, the work carries on, and the sentence
/// names the job. <see cref="SubmitAsync(ImageGenerationRequest, CancellationToken)"/> hands back
/// that job immediately instead.
/// </para>
/// <para>
/// <b>Content is read once.</b> <see cref="OpenContentAsync"/> unlinks the bytes at the hub as it
/// reads them: a second fetch is a <c>410</c>, and so is a retried one. This client never re-sends
/// that request, and it hands over the live stream rather than buffering somebody's picture.
/// </para>
/// <para>
/// Failures arrive in the OpenAI envelope and surface as
/// <see cref="Exceptions.InferHubOpenAiException"/> with <c>ErrorCode</c> and <c>Param</c> intact.
/// Three carry a <c>Retry-After</c> that <see cref="Exceptions.InferHubException.RetryAfter"/>
/// surfaces: <c>capability_unavailable</c> (the fleet holds the model but no node is currently
/// rendering — <b>not</b> a <c>404</c>), <c>queue_full</c>, and <c>job_still_running</c>.
/// </para>
/// </remarks>
public interface IInferHubImagesClient
{
    /// <summary>
    /// Draw something and wait for it — <c>POST /v1/images/generations</c>.
    /// </summary>
    /// <remarks>
    /// The picture comes back base64 in the envelope, because the hub stores nothing and so has no
    /// URL to serve. For a render that outlives an HTTP connection, submit it as a job.
    /// </remarks>
    /// <param name="request">Model, prompt, size, count and the <see cref="ImageOptions"/> knobs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ImageResponse> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Change a picture and wait for it — <c>POST /v1/images/edits</c>. With a mask, only the masked
    /// area is redrawn; without one, this is image-to-image.
    /// </summary>
    /// <param name="request">The picture, the prompt, and optionally a mask.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ImageResponse> EditAsync(ImageEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// More of this picture, and wait for it — <c>POST /v1/images/variations</c>. No prompt, no mask:
    /// see <see cref="ImageVariationRequest"/>.
    /// </summary>
    /// <param name="request">The picture to vary.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ImageResponse> CreateVariationAsync(ImageVariationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submit a generation as a job — <c>POST /api/images/jobs</c>, JSON. Returns as soon as the hub
    /// has accepted it, with a place in line.
    /// </summary>
    /// <param name="request">Model, prompt, size, count and the <see cref="ImageOptions"/> knobs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MediaJob> SubmitAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submit an edit as a job — <c>POST /api/images/jobs</c>, multipart with <c>operation=edit</c>.
    /// </summary>
    /// <param name="request">The picture, the prompt, and optionally a mask.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MediaJob> SubmitAsync(ImageEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submit a variation as a job — <c>POST /api/images/jobs</c>, multipart with
    /// <c>operation=variation</c>.
    /// </summary>
    /// <param name="request">The picture to vary.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MediaJob> SubmitAsync(ImageVariationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// This client's image jobs, oldest first, with the queue's own depth beside them —
    /// <c>GET /api/images/jobs</c>.
    /// </summary>
    /// <remarks>
    /// Client-scoped, never a fleet-wide listing: holding a job id is how a picture is fetched, so
    /// listing other tenants' ids would be handing them out. <b>It lists work, not results</b> — a
    /// job whose images have been delivered is still here and has nothing left to fetch.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MediaJobList> ListJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One job — <c>GET /api/images/jobs/{id}</c>. <c>null</c> when there is no such job.
    /// </summary>
    /// <remarks>
    /// A job that is not yours is the same <c>404</c> as one that does not exist, byte for byte, so
    /// <c>null</c> means "not yours or not there" and cannot be used to learn which.
    /// </remarks>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MediaJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Watch a job to its end — <c>GET /api/images/jobs/{id}/events</c>, server-sent events, one
    /// <see cref="MediaJob"/> per frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each frame is the whole job document as it stands, so a caller reads <c>state</c>,
    /// <c>step</c> and <c>totalSteps</c> off it without a second request. The hub re-sends the
    /// current state every 15 seconds as its keep-alive, which is why a client that reconnected
    /// mid-render needs no catch-up call.
    /// </para>
    /// <para>
    /// <b>The stream ends when the job does</b> — the last frame is the terminal one, and the
    /// enumeration completes rather than hanging. Walking away does not cancel the job: watching is
    /// not owning. A job that is already finished yields its terminal frame and ends.
    /// </para>
    /// </remarks>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the watch, not the render.</param>
    IAsyncEnumerable<MediaJob> WatchJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch one image of a finished job — <c>GET /api/images/jobs/{id}/content/{index}</c>.
    /// <b>Reads it once.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The read unlinks the bytes at the hub. There is no second copy, no retry, and no way to ask
    /// for it again: a repeat is a <c>410</c> with code <c>job_expired</c>, which is a different
    /// condition from <c>404</c> and says so. Dispose the result — it holds the live response.
    /// </para>
    /// <para>
    /// Two other refusals are worth catching by code: <c>409 job_not_ready</c> (the job has not
    /// succeeded yet) and <c>404 image_not_found</c> (this job has no image at that index).
    /// </para>
    /// </remarks>
    /// <param name="jobId">The job id.</param>
    /// <param name="index">Which image, from <see cref="MediaJobOutput.Index"/>.</param>
    /// <param name="cancellationToken">Cancels the request, and the read while the caller holds the stream.</param>
    Task<ImageContent> OpenContentAsync(string jobId, int index, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ask a job to stop — <c>DELETE /api/images/jobs/{id}</c>.
    /// </summary>
    /// <remarks>
    /// <b>Best effort, and the returned job says what actually happened.</b> A job cancelled at step
    /// 27 of 28 may still succeed, and a caller who then reads <c>succeeded</c> gets its image —
    /// discarding a finished render to honour a state name would be the worse answer. A job that is
    /// already terminal is a <c>409</c> with code <c>job_terminal</c>.
    /// </remarks>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MediaJob> CancelJobAsync(string jobId, CancellationToken cancellationToken = default);
}
