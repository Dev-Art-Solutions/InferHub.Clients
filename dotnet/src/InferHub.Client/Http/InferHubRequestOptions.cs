namespace InferHub.Client.Http;

/// <summary>
/// Per-request markers this client attaches to an <see cref="HttpRequestMessage"/> for its own
/// handlers to read.
/// </summary>
/// <remarks>
/// <see cref="HttpRequestOptionsKey{TValue}"/> rather than a header: this never reaches the wire,
/// and a header would be a hub-facing contract invented by a client.
/// </remarks>
internal static class InferHubRequestOptions
{
    /// <summary>
    /// "Do not re-send this request, whatever happens."
    /// </summary>
    /// <remarks>
    /// <b>Read-once content is the reason this exists.</b> <c>GET …/content/{index}</c> is an
    /// idempotent method by every rule <see cref="TransientRetryHandler"/> knows, and it is the one
    /// request in this client where a retry is destructive: the first read unlinks the bytes at the
    /// hub, so a connection that drops mid-body and is re-sent gets a <c>410</c> and the picture is
    /// gone for good. Marking the request beats documenting the hazard — a caveat that says "do not
    /// enable retries if you fetch image content" is true, unreadable, and one release from being
    /// forgotten.
    /// </remarks>
    public static readonly HttpRequestOptionsKey<bool> NeverRetry = new("InferHub.NeverRetry");

    public static void MarkNeverRetry(HttpRequestMessage request) =>
        request.Options.Set(NeverRetry, true);

    public static bool IsNeverRetry(HttpRequestMessage request) =>
        request.Options.TryGetValue(NeverRetry, out var value) && value;
}
