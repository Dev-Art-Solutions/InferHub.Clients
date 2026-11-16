using InferHub.Client.Exceptions;
using InferHub.Client.Models.Jobs;
using InferHub.Client.Models.Ollama;

namespace InferHub.Client;

/// <summary>
/// Text jobs — <c>/api/jobs</c>, hub v3.103+. A chat or a generate submitted as a job: the hub
/// answers at once with an id, runs the request the way <c>/api/chat</c> would, and keeps a status
/// that the caller, the node running it and the hub all update.
/// </summary>
/// <remarks>
/// <para>
/// For an answer that takes minutes — a big model, a model split across several nodes, a fleet
/// that is full — where one held connection is the wrong shape. A job also waits out the hub's
/// <c>503</c>s (a full fleet, a group reassembling) instead of answering them, and has its own,
/// longer deadline.
/// </para>
/// <para>
/// <b>Hub only.</b> A solo node has no <c>/api/jobs</c>; a submit there throws its 404. Everything
/// is in the hub's memory: a finished job stays for its retention (ten minutes by default) and a
/// hub restart loses every job.
/// </para>
/// </remarks>
public interface IInferHubJobsClient
{
    /// <summary><c>POST /api/jobs</c> with a chat. Returns at once with the job's status.</summary>
    /// <param name="request">The request <c>/api/chat</c> takes. Its <c>Stream</c> flag is ignored.</param>
    /// <param name="cancellationToken">Cancels the submit, not the job.</param>
    Task<Job> SubmitAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>POST /api/jobs</c> with a generate. Returns at once with the job's status.</summary>
    /// <param name="request">The request <c>/api/generate</c> takes. Its <c>Stream</c> flag is ignored.</param>
    /// <param name="cancellationToken">Cancels the submit, not the job.</param>
    Task<Job> SubmitAsync(GenerateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /api/jobs/{id}</c>. <c>null</c> for an id the hub does not hold for this client — not
    /// yours, finished and past its retention, or lost to a restart: one 404 by design.
    /// </summary>
    /// <param name="jobId">The id a submit returned.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<Job?> GetAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary><c>GET /api/jobs</c> — this client's jobs, newest first.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<IReadOnlyList<Job>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /api/jobs/{id}/result</c>. While the job runs: <see cref="JobResult.Done"/> is
    /// <see langword="false"/> and the result holds the text so far.
    /// </summary>
    /// <param name="jobId">The id a submit returned.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="InferHubException">
    /// The job failed (its own status — the one the blocking route would have answered), was
    /// cancelled (<c>409</c>), or is not there (<c>404</c>).
    /// </exception>
    Task<JobResult> GetResultAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>DELETE /api/jobs/{id}</c>. A running job is cancelled — the node is told to stop — and its
    /// status returned. A finished job is dropped now, with its answer, and <c>null</c> returned.
    /// </summary>
    /// <param name="jobId">The id a submit returned.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<Job?> CancelAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>GET /api/jobs/{id}/events</c> — the status each time it changes, ending after the terminal
    /// one. Server-sent events; where a proxy buffers them, use <see cref="WaitAsync"/>.
    /// </summary>
    /// <param name="jobId">The id a submit returned.</param>
    /// <param name="cancellationToken">Stops watching. The job keeps running.</param>
    IAsyncEnumerable<Job> WatchAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Polls the status until the job is finished, and returns it.</summary>
    /// <param name="jobId">The id a submit returned.</param>
    /// <param name="options">The poll interval; one second when <c>null</c>.</param>
    /// <param name="cancellationToken">Stops waiting. The job keeps running.</param>
    /// <exception cref="InferHubException">The hub no longer holds the job (<c>404</c>).</exception>
    Task<Job> WaitAsync(string jobId, JobWaitOptions? options = null, CancellationToken cancellationToken = default);
}
