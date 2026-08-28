using System.Net;
using InferHub.Client.Configuration;

namespace InferHub.Client.Http;

/// <summary>
/// Off-by-default transient-failure retry for <b>idempotent</b> requests only. Retries a
/// <c>GET</c> or <c>HEAD</c> that fails with a connection error (<see cref="HttpRequestException"/>)
/// or a retryable status (<c>5xx</c> or <c>408 Request Timeout</c>), up to
/// <see cref="InferHubClientOptions.MaxRetryAttempts"/> times with exponential back-off.
/// Mutating and streaming calls (chat/generate/upsert/delete, POST/PUT/DELETE) are passed
/// straight through, so nothing is ever silently re-run and a stream never retries mid-flight.
/// </summary>
internal sealed class TransientRetryHandler : DelegatingHandler
{
    private readonly InferHubClientOptions options;

    public TransientRetryHandler(InferHubClientOptions options)
    {
        this.options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var maxAttempts = options.MaxRetryAttempts;
        if (maxAttempts <= 0 || !IsIdempotent(request.Method))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var delay = options.RetryBaseDelay;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (attempt >= maxAttempts || !IsRetryableStatus(response.StatusCode))
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                // Connection-level failure (coordinator down / restarting) — retry.
            }

            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaxRetryDelay.Ticks));
        }
    }

    private static bool IsIdempotent(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head;

    private static bool IsRetryableStatus(HttpStatusCode status)
        => status == HttpStatusCode.RequestTimeout || (int)status >= 500;
}
