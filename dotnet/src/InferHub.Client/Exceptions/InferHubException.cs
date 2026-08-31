using System.Net;

namespace InferHub.Client.Exceptions;

/// <summary>
/// Thrown when the coordinator returns a non-success HTTP response. Carries the raw
/// status code so callers can distinguish 404 (model or collection missing), 401/403
/// (auth), and 424 (retrieval unavailable, from later phases).
/// </summary>
public class InferHubException : Exception
{
    /// <summary>The HTTP status code returned by the coordinator.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw response body (usually <c>{ "error": "…" }</c>), or empty.</summary>
    public string ResponseBody { get; }

    /// <summary>
    /// How long the hub asked the caller to wait, from <c>Retry-After</c>. <c>null</c> when it sent
    /// none, which is most refusals.
    /// </summary>
    /// <remarks>
    /// The refusals that do carry one are the ones a caller can act on rather than only report:
    /// <c>503 capability_unavailable</c> ("the fleet holds this model but no node is doing this kind
    /// of work"), <c>503 queue_full</c>, and the synchronous image route's <c>503
    /// job_still_running</c>. Reading it here rather than per client is why the second surface that
    /// needs it does not re-implement it.
    /// </remarks>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Create a new <see cref="InferHubException"/>.</summary>
    public InferHubException(HttpStatusCode statusCode, string message, string responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
