using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using InferHub.Client.Configuration;
using InferHub.Client.Exceptions;
using InferHub.Client.Http;
using InferHub.Client.Models.Images;
using InferHub.Client.Models.Videos;
using InferHub.Client.Serialization;

namespace InferHub.Client;

/// <inheritdoc cref="IInferHubVideoClient"/>
public sealed class InferHubVideoClient : IInferHubVideoClient
{
    private const string VideosPath = "v1/videos";
    private const string JobsPath = "api/videos/jobs";

    private static InferHubJsonContext Json => InferHubJsonContext.Default;

    private readonly HttpClient httpClient;
    private readonly TimeSpan requestTimeout;

    /// <summary>
    /// Create a new client. Prefer <c>services.AddInferHubClient(...)</c> in DI, which registers
    /// this client with an infinite <see cref="HttpClient.Timeout"/> — fetching a clip is a download
    /// of tens of megabytes the caller reads after the call returns — and applies
    /// <see cref="InferHubClientOptions.Timeout"/> to the short calls instead.
    /// </summary>
    /// <param name="httpClient">Transport. Set <c>Timeout = Timeout.InfiniteTimeSpan</c> when constructing this by hand.</param>
    /// <param name="options">Client options; <c>null</c> means no per-call timeout.</param>
    public InferHubVideoClient(HttpClient httpClient, InferHubClientOptions? options = null)
    {
        this.httpClient = httpClient;
        requestTimeout = options?.Timeout ?? Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc/>
    public async Task<Video> CreateAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The two guards the hub answers with a 400 anyway, checked here because a round trip to be
        // told "prompt is required" is a round trip. The size is deliberately NOT checked: the
        // recipe's catalogue is narrower than the grid rule and only the worker knows it.
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, VideosPath)
            {
                Content = JsonContent.Create(request, Json.VideoGenerationRequest)
            };

            if (request.Options is { } options)
            {
                foreach (var (name, value) in options.ToHeaders())
                {
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }

            using var response = await SendAsync(message, token);
            return await ReadVideoAsync(response, token);
        });
    }

    /// <inheritdoc/>
    public async Task<Video?> GetAsync(string videoId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, VideoPath(videoId));
            using var response = await httpClient.SendAsync(message, token);

            // "Not yours", "not there" and "that id is an image job" are one 404 by design, so null
            // means one of the three and cannot be used to tell which.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await InferHubResponse.EnsureSuccessAsync(response, token);

            return await ReadVideoAsync(response, token);
        });
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Video> WatchAsync(
        string videoId,
        VideoWatchOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);

        var settings = options ?? new VideoWatchOptions();
        var interval = settings.PollInterval > TimeSpan.Zero ? settings.PollInterval : TimeSpan.FromSeconds(2);

        string? previous = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var video = await GetAsync(videoId, cancellationToken)
                ?? throw new InferHubOpenAiException(
                    HttpStatusCode.NotFound,
                    $"video '{videoId}' not found",
                    string.Empty,
                    errorType: "not_found_error",
                    errorCode: VideoErrorCodes.NotFound,
                    param: "id");

            // "Something new" is the status plus the percentage: those are the two fields that move,
            // and a caller watching a five-minute render does not want two hundred identical lines.
            var current = $"{video.Status}/{video.Progress}";

            if (settings.YieldUnchanged || video.IsTerminal || current != previous)
            {
                previous = current;
                yield return video;
            }

            if (video.IsTerminal)
            {
                yield break;
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<VideoContent> OpenContentAsync(string videoId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);

        using var message = new HttpRequestMessage(HttpMethod.Get, $"{VideoPath(videoId)}/content");

        // The read unlinks the bytes at the hub, so this is one of the two requests in this client
        // that must never be re-sent: a retry after a dropped connection collects a 410 and the clip
        // is gone. The marker is honoured by TransientRetryHandler whatever the caller configured.
        InferHubRequestOptions.MarkNeverRetry(message);

        // No per-call timeout: the caller reads the body after this method returns, and a timer still
        // running then would abort the download of bytes that no longer exist anywhere else.
        var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        try
        {
            await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            return new VideoContent(
                response,
                stream,
                response.Content.Headers.ContentType?.ToString() ?? "video/mp4",
                response.Content.Headers.ContentLength,
                InferHubHeaders.ReadServedBy(response));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<VideoDeletion> DeleteAsync(string videoId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Delete, VideoPath(videoId));
            using var response = await SendAsync(message, token);

            return await response.Content.ReadFromJsonAsync(Json.VideoDeletion, token)
                ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
        });
    }

    /// <inheritdoc/>
    public async Task<MediaJobList> ListJobsAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, JobsPath);
            using var response = await SendAsync(message, token);

            return await response.Content.ReadFromJsonAsync(Json.MediaJobList, token)
                ?? new MediaJobList();
        });
    }

    // ---- plumbing --------------------------------------------------------------------------

    /// <summary>
    /// The path for one clip. The id is escaped rather than trusted: it comes back from the hub, but
    /// a caller may well have kept it in a database and typed it back in.
    /// </summary>
    private static string VideoPath(string videoId) => $"{VideosPath}/{Uri.EscapeDataString(videoId.Trim())}";

    private static async Task<Video> ReadVideoAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var video = await response.Content.ReadFromJsonAsync(Json.Video, cancellationToken)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);

        video.ServedBy = InferHubHeaders.ReadServedBy(response);
        return video;
    }

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

    /// <summary>
    /// Translate a per-call timeout expiry into <see cref="TimeoutException"/>, so a caller can tell
    /// "I cancelled" from "the hub took too long".
    /// </summary>
    private async Task<T> WithTimeoutAsync<T>(CancellationToken callerToken, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && requestTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new TimeoutException($"The InferHub video request timed out after {requestTimeout.TotalSeconds:0.#}s.");
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
