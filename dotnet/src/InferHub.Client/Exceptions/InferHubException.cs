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

    /// <summary>Create a new <see cref="InferHubException"/>.</summary>
    public InferHubException(HttpStatusCode statusCode, string message, string responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
