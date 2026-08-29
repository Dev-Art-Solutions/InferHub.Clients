using System.Net;

namespace InferHub.Client.Exceptions;

/// <summary>
/// Thrown when a request to the hub's OpenAI-compatible surface (<c>/v1/*</c>) fails. That dialect
/// answers with its own envelope — <c>{"error":{"message":…,"type":…,"code":…,"param":…}}</c> —
/// rather than the Ollama surface's <c>{"error":"…"}</c>, so the extra fields are surfaced here
/// instead of being flattened into a message. Derives from <see cref="InferHubException"/>, so a
/// broad <c>catch (InferHubException)</c> still catches it.
/// </summary>
/// <remarks>
/// A refused <c>X-InferHub-Provider</c> steer arrives here as a <c>400</c> with
/// <see cref="ErrorType"/> <c>invalid_request_error</c>. The hub answers the <em>same</em> sentence
/// for an unknown provider, a disabled one and a real one that maps a different model — it will not
/// let a client with an inference key enumerate the operator's vendors — so there is deliberately
/// no exception type per condition.
/// </remarks>
public sealed class InferHubOpenAiException : InferHubException
{
    /// <summary>Create a new <see cref="InferHubOpenAiException"/>.</summary>
    /// <param name="statusCode">HTTP status returned by the hub.</param>
    /// <param name="message">The envelope's <c>error.message</c>.</param>
    /// <param name="responseBody">The raw response body.</param>
    /// <param name="errorType">The envelope's <c>error.type</c>.</param>
    /// <param name="errorCode">The envelope's <c>error.code</c>.</param>
    /// <param name="param">The envelope's <c>error.param</c> — which request field is at fault.</param>
    public InferHubOpenAiException(
        HttpStatusCode statusCode,
        string message,
        string responseBody,
        string? errorType = null,
        string? errorCode = null,
        string? param = null)
        : base(statusCode, message, responseBody)
    {
        ErrorType = errorType;
        ErrorCode = errorCode;
        Param = param;
    }

    /// <summary>
    /// <c>error.type</c> — <c>invalid_request_error</c>, <c>not_found_error</c>,
    /// <c>rate_limit_error</c> or <c>api_error</c>.
    /// </summary>
    public string? ErrorType { get; }

    /// <summary>
    /// <c>error.code</c> — e.g. <c>model_not_found</c>, <c>rate_limit_exceeded</c>,
    /// <c>retrieval_unavailable</c>. Always exposed as a string: an upstream passed through by the
    /// hub may write it as a JSON number (OpenAI writes <c>"rate_limit_exceeded"</c>, OpenRouter
    /// writes <c>429</c>), and both are read.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary><c>error.param</c> — the request field the hub is complaining about, when it names one.</summary>
    public string? Param { get; }
}
