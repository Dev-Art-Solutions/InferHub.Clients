using System.Net;

namespace InferHub.Client.Exceptions;

/// <summary>
/// Thrown when a grounded chat/generate call fails because retrieval could not be satisfied —
/// HTTP <c>424 Failed Dependency</c>. Raised when the coordinator is configured with
/// <c>OnMissing=error</c> and the retrieval step is unavailable or returns nothing. Derives from
/// <see cref="InferHubException"/>, so a broad <c>catch (InferHubException)</c> still catches it;
/// catch this type first to distinguish "retrieval failed" from other request failures.
/// </summary>
public sealed class InferHubRetrievalException : InferHubException
{
    /// <summary>Create a new <see cref="InferHubRetrievalException"/> (always status <c>424</c>).</summary>
    public InferHubRetrievalException(string message, string responseBody)
        : base(HttpStatusCode.FailedDependency, message, responseBody)
    {
    }
}
