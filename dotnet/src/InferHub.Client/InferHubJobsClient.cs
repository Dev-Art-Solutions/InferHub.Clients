using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using InferHub.Client.Configuration;
using InferHub.Client.Exceptions;
using InferHub.Client.Http;
using InferHub.Client.Models.Jobs;
using InferHub.Client.Models.Ollama;
using InferHub.Client.Serialization;

namespace InferHub.Client;

/// <inheritdoc cref="IInferHubJobsClient"/>
public sealed class InferHubJobsClient : IInferHubJobsClient
{
    private const string JobsPath = "api/jobs";

    private static InferHubJsonContext Json => InferHubJsonContext.Default;

    private readonly HttpClient httpClient;
    private readonly TimeSpan requestTimeout;

    /// <summary>
    /// Create a new client. Prefer <c>services.AddInferHubClient(...)</c> in DI, which registers this
    /// client with an infinite <see cref="HttpClient.Timeout"/> — the status stream lives as long as
    /// the job does — and applies <see cref="InferHubClientOptions.Timeout"/> to the short calls.
    /// </summary>
    /// <param name="httpClient">Transport. Set <c>Timeout = Timeout.InfiniteTimeSpan</c> when constructing this by hand.</param>
    /// <param name="options">Client options; <c>null</c> means no per-call timeout.</param>
    public InferHubJobsClient(HttpClient httpClient, InferHubClientOptions? options = null)
    {
        this.httpClient = httpClient;
        requestTimeout = options?.Timeout ?? Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc/>
    public Task<Job> SubmitAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);

        return SubmitAsync(
            JsonContent.Create(new ChatJobSubmission("chat", request), Json.ChatJobSubmission),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Job> SubmitAsync(GenerateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);

        return SubmitAsync(
            JsonContent.Create(new GenerateJobSubmission("generate", request), Json.GenerateJobSubmission),
            cancellationToken);
    }

    private async Task<Job> SubmitAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, JobsPath) { Content = content };

            // A submit that was accepted and whose answer was lost must not be sent again: the hub
            // would run the request twice.
            InferHubRequestOptions.MarkNeverRetry(message);

            using var response = await SendAsync(message, token);
            return await ReadJobAsync(response, token);
        });
    }

    /// <inheritdoc/>
    public async Task<Job?> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, JobPath(jobId));
            using var response = await httpClient.SendAsync(message, token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await InferHubResponse.EnsureSuccessAsync(response, token);
            return await ReadJobAsync(response, token);
        });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Job>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync<IReadOnlyList<Job>>(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, JobsPath);
            using var response = await SendAsync(message, token);

            return (await response.Content.ReadFromJsonAsync(Json.JobList, token))?.Jobs ?? [];
        });
    }

    /// <inheritdoc/>
    public async Task<JobResult> GetResultAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"{JobPath(jobId)}/result");
            using var response = await SendAsync(message, token);

            var body = await response.Content.ReadAsStringAsync(token);
            var servedBy = InferHubHeaders.ReadServedBy(response);
            var done = response.StatusCode == HttpStatusCode.OK;

            // The kind is read from the body rather than asked for: a chat answer has a message,
            // a generate answer has a response.
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("message", out _))
            {
                var chat = JsonSerializer.Deserialize(body, Json.ChatResponse)
                    ?? throw new InferHubException(response.StatusCode, "empty response body", body);
                chat.ServedBy = servedBy;
                return new JobResult { Done = done, Chat = chat, ServedBy = servedBy };
            }

            var generate = JsonSerializer.Deserialize(body, Json.GenerateResponse)
                ?? throw new InferHubException(response.StatusCode, "empty response body", body);
            generate.ServedBy = servedBy;
            return new JobResult { Done = done, Generate = generate, ServedBy = servedBy };
        });
    }

    /// <inheritdoc/>
    public async Task<Job?> CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Delete, JobPath(jobId));
            using var response = await httpClient.SendAsync(message, token);

            // 204: a finished job was dropped. 404: there was nothing to cancel.
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            {
                return null;
            }

            await InferHubResponse.EnsureSuccessAsync(response, token);
            return await ReadJobAsync(response, token);
        });
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Job> WatchAsync(
        string jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var message = new HttpRequestMessage(HttpMethod.Get, $"{JobPath(jobId)}/events");
        message.Headers.Accept.ParseAdd("text/event-stream");

        // No per-call timeout: the stream lives as long as the job, and the caller's token governs it.
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var frame in SseFrameReader.ReadAsync(stream, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(frame.Data))
            {
                continue;
            }

            Job? job;

            try
            {
                job = JsonSerializer.Deserialize(frame.Data, Json.Job);
            }
            catch (JsonException)
            {
                continue;
            }

            if (job is null)
            {
                continue;
            }

            yield return job;

            if (job.IsTerminal)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<Job> WaitAsync(string jobId, JobWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var interval = options?.PollInterval is { } asked && asked >= TimeSpan.Zero ? asked : TimeSpan.FromSeconds(1);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = await GetAsync(jobId, cancellationToken)
                ?? throw new InferHubException(
                    HttpStatusCode.NotFound,
                    $"job '{jobId}' not found — it finished and its retention passed, the hub restarted, or it was never this client's",
                    string.Empty);

            if (job.IsTerminal)
            {
                return job;
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    // ---- plumbing --------------------------------------------------------------------------

    private static string JobPath(string jobId) => $"{JobsPath}/{Uri.EscapeDataString(jobId.Trim())}";

    private static async Task<Job> ReadJobAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => await response.Content.ReadFromJsonAsync(Json.Job, cancellationToken)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        var response = await httpClient.SendAsync(message, cancellationToken);

        try
        {
            await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<T> WithTimeoutAsync<T>(CancellationToken callerToken, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && requestTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new TimeoutException($"The InferHub job request timed out after {requestTimeout.TotalSeconds:0.#}s.");
        }
    }

    private CancellationTokenSource? StartRequestTimeout(CancellationToken cancellationToken, out CancellationToken token)
    {
        if (requestTimeout == Timeout.InfiniteTimeSpan)
        {
            token = cancellationToken;
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(requestTimeout);
        token = cts.Token;
        return cts;
    }
}
