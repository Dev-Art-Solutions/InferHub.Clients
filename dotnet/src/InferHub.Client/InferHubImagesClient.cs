using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using InferHub.Client.Configuration;
using InferHub.Client.Exceptions;
using InferHub.Client.Http;
using InferHub.Client.Models.Images;
using InferHub.Client.Serialization;

namespace InferHub.Client;

/// <inheritdoc cref="IInferHubImagesClient"/>
public sealed class InferHubImagesClient : IInferHubImagesClient
{
    private const string JobsPath = "api/images/jobs";

    private static InferHubJsonContext Json => InferHubJsonContext.Default;

    private readonly HttpClient httpClient;
    private readonly TimeSpan requestTimeout;

    /// <summary>
    /// Create a new client. Prefer <c>services.AddInferHubClient(...)</c> in DI, which registers
    /// this client with an infinite <see cref="HttpClient.Timeout"/> — a render takes minutes and
    /// the job watch is long-lived, so an <see cref="HttpClient"/> timeout would abort both — and
    /// applies <see cref="InferHubClientOptions.Timeout"/> to the short calls instead.
    /// </summary>
    /// <param name="httpClient">Transport. Set <c>Timeout = Timeout.InfiniteTimeSpan</c> when constructing this by hand.</param>
    /// <param name="options">Client options; <c>null</c> means no per-call timeout.</param>
    public InferHubImagesClient(HttpClient httpClient, InferHubClientOptions? options = null)
    {
        this.httpClient = httpClient;
        requestTimeout = options?.Timeout ?? Timeout.InfiniteTimeSpan;
    }

    // ---- the synchronous /v1 routes --------------------------------------------------------

    /// <inheritdoc/>
    public async Task<ImageResponse> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/images/generations")
        {
            Content = JsonContent.Create(request, Json.ImageGenerationRequest)
        };

        ApplyOptions(message, request.Options);

        return await SendForImageAsync(message, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ImageResponse> EditAsync(ImageEditRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var content = BuildEditForm(request, operation: null);
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/images/edits") { Content = content };
        ApplyOptions(message, request.Options);

        return await SendForImageAsync(message, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ImageResponse> CreateVariationAsync(ImageVariationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var content = BuildVariationForm(request, operation: null);
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/images/variations") { Content = content };
        ApplyOptions(message, request.Options);

        return await SendForImageAsync(message, cancellationToken);
    }

    // ---- the job seam ----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<MediaJob> SubmitAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        using var message = new HttpRequestMessage(HttpMethod.Post, JobsPath)
        {
            Content = JsonContent.Create(request, Json.ImageGenerationRequest)
        };

        ApplyOptions(message, request.Options);

        return await SendForJobAsync(message, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MediaJob> SubmitAsync(ImageEditRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var content = BuildEditForm(request, ImageOperations.Edit);
        using var message = new HttpRequestMessage(HttpMethod.Post, JobsPath) { Content = content };
        ApplyOptions(message, request.Options);

        return await SendForJobAsync(message, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MediaJob> SubmitAsync(ImageVariationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var content = BuildVariationForm(request, ImageOperations.Variation);
        using var message = new HttpRequestMessage(HttpMethod.Post, JobsPath) { Content = content };
        ApplyOptions(message, request.Options);

        return await SendForJobAsync(message, cancellationToken);
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

    /// <inheritdoc/>
    public async Task<MediaJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, JobPath(jobId));
            using var response = await httpClient.SendAsync(message, token);

            // "Not yours" and "not there" are the same 404 by design, so null means one of the two
            // and cannot be used to tell which.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await InferHubResponse.EnsureSuccessAsync(response, token);

            return await ReadJobAsync(response, token);
        });
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MediaJob> WatchJobAsync(
        string jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var message = new HttpRequestMessage(HttpMethod.Get, $"{JobPath(jobId)}/events");

        // No per-call timeout: a render is minutes and the caller's token governs the watch.
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);

        var servedBy = InferHubHeaders.ReadServedBy(response);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var frame in SseFrameReader.ReadAsync(stream, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(frame.Data))
            {
                continue;
            }

            MediaJob? job;

            try
            {
                job = JsonSerializer.Deserialize(frame.Data, Json.MediaJob);
            }
            catch (JsonException ex)
            {
                throw new InferHubException(response.StatusCode, $"Malformed SSE frame: {ex.Message}", frame.Data);
            }

            if (job is null)
            {
                continue;
            }

            // The hub writes the state on the `event:` line as well as in the payload. The payload
            // is authoritative; the event name fills in only if a future frame ever omits it.
            job.State = string.IsNullOrWhiteSpace(job.State) ? frame.Event ?? string.Empty : job.State;
            job.ServedBy = servedBy;

            yield return job;

            // The hub closes the stream after the terminal frame. Stopping here as well means a
            // caller's loop ends on the frame that answered their question rather than on an
            // end-of-body they have to wait for.
            if (job.IsTerminal)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<ImageContent> OpenContentAsync(string jobId, int index, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"{JobPath(jobId)}/content/{index.ToString(CultureInfo.InvariantCulture)}");

        // The read unlinks the bytes at the hub, so this is the one request in this client that must
        // never be re-sent: a retry after a dropped connection collects a 410 and the picture is
        // gone. The marker is honoured by TransientRetryHandler whatever the caller configured.
        InferHubRequestOptions.MarkNeverRetry(message);

        // No per-call timeout: the caller reads the body after this method returns, and a timer
        // still running then would abort the download of bytes that no longer exist anywhere else.
        var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        try
        {
            await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            return new ImageContent(
                response,
                stream,
                response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
                response.Content.Headers.ContentLength,
                InferHubHeaders.ReadString(response, ImageHeaders.Projection) ?? ImageProjections.Flat,
                InferHubHeaders.ReadString(response, ImageHeaders.SeamRepair),
                InferHubHeaders.ReadDouble(response, ImageHeaders.SeamDelta),
                InferHubHeaders.ReadDouble(response, ImageHeaders.SeamDeltaBefore),
                InferHubHeaders.ReadServedBy(response));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<MediaJob> CancelJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var message = new HttpRequestMessage(HttpMethod.Delete, JobPath(jobId));

        return await SendForJobAsync(message, cancellationToken);
    }

    // ---- plumbing --------------------------------------------------------------------------

    private static string JobPath(string jobId) => $"{JobsPath}/{Uri.EscapeDataString(jobId)}";

    /// <summary>
    /// The guards the hub would answer with a <c>400</c> anyway, checked here because a round trip
    /// to be told "prompt is required" is a round trip.
    /// </summary>
    private static void Validate(ImageGenerationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
    }

    private static void ApplyOptions(HttpRequestMessage message, ImageOptions? options)
    {
        if (options is null)
        {
            return;
        }

        foreach (var (name, value) in options.ToHeaders())
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }
    }

    /// <summary>
    /// An edit's multipart body, with <b>every field written before the file parts</b>.
    /// </summary>
    /// <remarks>
    /// Above the hub's <c>Tools:MaxStreamedBytes</c> the request is routed from the leading fields
    /// while the bytes are still arriving, so a field after the file is refused with a <c>400</c>
    /// naming it. The buffered path below that ceiling tolerates any order, which is what makes the
    /// mistake dangerous: a client that writes the file first is correct on every small test image
    /// and wrong on the first real one, in production.
    /// </remarks>
    private static MultipartFormDataContent BuildEditForm(ImageEditRequest request, string? operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

        if (request.Image is null)
        {
            throw new ArgumentException($"{nameof(ImageEditRequest.Image)} is required.", nameof(request));
        }

        var content = new MultipartFormDataContent();

        try
        {
            if (operation is not null)
            {
                content.Add(new StringContent(operation), "operation");
            }

            content.Add(new StringContent(request.Model), "model");
            content.Add(new StringContent(request.Prompt), "prompt");

            AddOptionalFields(content, request.NegativePrompt, request.Count, request.Size, request.ResponseFormat);

            // Last, always — and the mask after the image, because the hub reads the image part
            // first and a mask with no picture is a refusal either way.
            AddFile(content, request.Image, request.ImageContentType, "image");

            if (request.Mask is not null)
            {
                AddFile(content, request.Mask, request.MaskContentType, "mask");
            }

            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    private static MultipartFormDataContent BuildVariationForm(ImageVariationRequest request, string? operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);

        if (request.Image is null)
        {
            throw new ArgumentException($"{nameof(ImageVariationRequest.Image)} is required.", nameof(request));
        }

        var content = new MultipartFormDataContent();

        try
        {
            if (operation is not null)
            {
                content.Add(new StringContent(operation), "operation");
            }

            content.Add(new StringContent(request.Model), "model");

            AddOptionalFields(content, negativePrompt: null, request.Count, request.Size, request.ResponseFormat);

            AddFile(content, request.Image, request.ImageContentType, "image");

            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    private static void AddOptionalFields(
        MultipartFormDataContent content,
        string? negativePrompt,
        int? count,
        string? size,
        string? responseFormat)
    {
        if (!string.IsNullOrWhiteSpace(negativePrompt))
        {
            content.Add(new StringContent(negativePrompt), "negative_prompt");
        }

        if (count is { } n)
        {
            content.Add(new StringContent(n.ToString(CultureInfo.InvariantCulture)), "n");
        }

        if (!string.IsNullOrWhiteSpace(size))
        {
            content.Add(new StringContent(size), "size");
        }

        if (!string.IsNullOrWhiteSpace(responseFormat))
        {
            content.Add(new StringContent(responseFormat), "response_format");
        }
    }

    /// <summary>
    /// One file part, named by its <b>role</b>. The caller's own file name is deliberately not sent:
    /// what somebody called a file on their disk is content in the sense rule 5 means, and the hub
    /// drops it too.
    /// </summary>
    private static void AddFile(MultipartFormDataContent content, Stream stream, string? contentType, string role)
    {
        var file = new StreamContent(stream);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        content.Add(file, role, role);
    }

    private async Task<ImageResponse> SendForImageAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        // No per-call timeout on the synchronous routes: the hub holds the connection until the
        // render finishes or its own Images:SyncMaxWaitSeconds expires, and that budget is the
        // operator's to set. The caller's token is what governs it here.
        using var response = await SendAsync(message, cancellationToken);

        var answer = await response.Content.ReadFromJsonAsync(Json.ImageResponse, cancellationToken)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);

        answer.ServedBy = InferHubHeaders.ReadServedBy(response);
        return answer;
    }

    private async Task<MediaJob> SendForJobAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var response = await SendAsync(message, token);
            return await ReadJobAsync(response, token);
        });
    }

    private static async Task<MediaJob> ReadJobAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var job = await response.Content.ReadFromJsonAsync(Json.MediaJob, cancellationToken)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);

        job.ServedBy = InferHubHeaders.ReadServedBy(response);
        return job;
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
            throw new TimeoutException($"The InferHub image request timed out after {requestTimeout.TotalSeconds:0.#}s.");
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

    /// <summary>
    /// The <c>operation</c> a multipart job submission must name. The synchronous <c>/v1</c> routes
    /// do not take it — there the route <em>is</em> the operation — and defaulting it on the job
    /// route would let a typo turn a variation into an edit.
    /// </summary>
    private static class ImageOperations
    {
        public const string Edit = "edit";
        public const string Variation = "variation";
    }
}
