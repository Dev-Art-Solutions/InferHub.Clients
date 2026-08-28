using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using InferHub.Client.Configuration;
using InferHub.Client.Exceptions;
using InferHub.Client.Models.Admin;
using InferHub.Client.Serialization;

namespace InferHub.Client;

/// <inheritdoc cref="IInferHubAdminClient"/>
public sealed class InferHubAdminClient : IInferHubAdminClient
{
    private static InferHubJsonContext Json => InferHubJsonContext.Default;

    private readonly HttpClient httpClient;
    private readonly TimeSpan requestTimeout;

    /// <summary>
    /// Create a new admin client. Prefer <c>services.AddInferHubClient(...)</c> in DI —
    /// it registers this client with an infinite <see cref="HttpClient.Timeout"/> (so the
    /// SSE stream can outlive it) and applies <see cref="InferHubClientOptions.Timeout"/>
    /// per non-streaming call instead. When constructing directly, do the same: set
    /// <c>httpClient.Timeout = Timeout.InfiniteTimeSpan</c> and pass
    /// <paramref name="options"/>, or leave both at their defaults and accept that
    /// <see cref="StreamAdminEventsAsync(CancellationToken)"/> is cut off by the client timeout.
    /// </summary>
    /// <param name="httpClient">HTTP client with <see cref="HttpClient.BaseAddress"/> set to the coordinator.</param>
    /// <param name="options">Options supplying the per-call timeout; <c>null</c> defers to <paramref name="httpClient"/>'s own timeout.</param>
    public InferHubAdminClient(HttpClient httpClient, InferHubClientOptions? options = null)
    {
        this.httpClient = httpClient;
        requestTimeout = options?.Timeout ?? Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AdminNode>> ListNodesAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync("api/admin/nodes", Json.AdminNodeArray, cancellationToken);
    }

    /// <inheritdoc/>
    public Task CordonAsync(string nodeId, CancellationToken cancellationToken = default)
        => PostNodeActionAsync(nodeId, "cordon", cancellationToken);

    /// <inheritdoc/>
    public Task UncordonAsync(string nodeId, CancellationToken cancellationToken = default)
        => PostNodeActionAsync(nodeId, "uncordon", cancellationToken);

    /// <inheritdoc/>
    public Task DeregisterAsync(string nodeId, CancellationToken cancellationToken = default)
        => PostNodeActionAsync(nodeId, "deregister", cancellationToken);

    /// <inheritdoc/>
    public async Task<CollectionsResponse> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync("api/admin/vector/collections", Json.CollectionsResponse, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CollectionDetail?> GetCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);
        using var response = await SendAsync(
            () => httpClient.GetAsync($"api/admin/vector/collections/{Escape(collection)}", token),
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await Http.InferHubResponse.EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync(Json.CollectionDetail, token)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
    }

    /// <inheritdoc/>
    public async Task<CollectionInfo> CreateCollectionAsync(string name, int dimension, string? distance = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimension, 0);

        var body = new CreateCollectionRequest { Name = name, Dimension = dimension, Distance = distance };

        using var timeout = StartRequestTimeout(cancellationToken, out var token);
        using var response = await SendAsync(
            () => httpClient.PostAsJsonAsync("api/admin/vector/collections", body, Json.CreateCollectionRequest, token),
            cancellationToken);
        await Http.InferHubResponse.EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync(Json.CollectionInfo, token)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
    }

    /// <inheritdoc/>
    public async Task DropCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);
        using var response = await SendAsync(
            () => httpClient.DeleteAsync($"api/admin/vector/collections/{Escape(collection)}", token),
            cancellationToken);
        await Http.InferHubResponse.EnsureSuccessAsync(response, token);
    }

    /// <inheritdoc/>
    public async Task RebuildAsync(string collection, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);
        using var response = await SendAsync(
            () => httpClient.PostAsync($"api/admin/vector/collections/{Escape(collection)}/rebuild", content: null, token),
            cancellationToken);
        await Http.InferHubResponse.EnsureSuccessAsync(response, token);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AdminEvent> StreamAdminEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // No per-request timeout here: the stream is long-lived by design (the server
        // sends a keepalive snapshot roughly every 10 seconds). Cancellation is the
        // only way out from the client side.
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/admin/stream");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await Http.InferHubResponse.EnsureSuccessAsync(response, cancellationToken);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? eventName = null;
        var data = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            if (line.Length == 0)
            {
                // Blank line terminates one SSE event.
                if (data.Length > 0)
                {
                    yield return ParseEvent(eventName, data.ToString());
                }

                eventName = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue; // SSE comment / keepalive
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line["data:".Length..].TrimStart());
            }
            // "id:" / "retry:" fields are ignored — the coordinator does not send them.
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AdminEvent> StreamAdminEventsAsync(
        AdminStreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var backoff = options.InitialBackoff;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var enumerator = StreamAdminEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await enumerator.MoveNextAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (InferHubException ex) when (
                        ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                    {
                        throw; // a bad admin key never fixes itself — do not retry
                    }
                    catch (Exception) when (options.Reconnect)
                    {
                        break; // transport/protocol failure — reconnect after backoff
                    }

                    if (!moved)
                    {
                        break; // server closed the stream cleanly
                    }

                    backoff = options.InitialBackoff; // healthy stream — reset the backoff
                    yield return enumerator.Current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            if (!options.Reconnect)
            {
                yield break;
            }

            await Task.Delay(backoff, cancellationToken);
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, options.MaxBackoff.Ticks));
        }
    }

    private static AdminEvent ParseEvent(string? eventName, string data)
    {
        try
        {
            if (eventName is null or "snapshot" or "message")
            {
                var payload = JsonSerializer.Deserialize(data, Json.AdminSnapshotPayload);
                return new AdminEvent
                {
                    Event = "snapshot",
                    Nodes = payload?.Nodes ?? Array.Empty<AdminNode>()
                };
            }

            var vector = JsonSerializer.Deserialize(data, Json.AdminVectorEventPayload);
            return new AdminEvent
            {
                Event = eventName,
                Sequence = vector?.Sequence,
                Collection = vector?.Collection,
                AtUtc = vector?.AtUtc,
                Data = vector?.Data
            };
        }
        catch (JsonException ex)
        {
            throw new InferHubException(System.Net.HttpStatusCode.OK, $"Malformed SSE event data: {ex.Message}", data);
        }
    }

    private async Task PostNodeActionAsync(string nodeId, string action, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);
        using var response = await SendAsync(
            () => httpClient.PostAsync($"api/admin/nodes/{Escape(nodeId)}/{action}", content: null, token),
            cancellationToken);
        await Http.InferHubResponse.EnsureSuccessAsync(response, token);
    }

    private async Task<TResult> GetAsync<TResult>(string path, JsonTypeInfo<TResult> resultInfo, CancellationToken cancellationToken)
    {
        using var timeout = StartRequestTimeout(cancellationToken, out var token);
        using var response = await SendAsync(() => httpClient.GetAsync(path, token), cancellationToken);
        await Http.InferHubResponse.EnsureSuccessAsync(response, token);
        var result = await response.Content.ReadFromJsonAsync(resultInfo, token);
        return result ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
    }

    /// <summary>
    /// Translate a per-call timeout expiry into <see cref="TimeoutException"/> so callers can
    /// tell it apart from their own cancellation (the DI-registered <see cref="HttpClient"/>
    /// has an infinite timeout to keep the SSE stream alive).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken callerToken)
    {
        try
        {
            return await send();
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && requestTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new TimeoutException($"The InferHub admin request timed out after {requestTimeout.TotalSeconds:0.#}s.");
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

    private static string Escape(string segment) => Uri.EscapeDataString(segment);
}
