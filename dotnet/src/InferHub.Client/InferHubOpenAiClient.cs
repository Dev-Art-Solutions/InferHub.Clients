using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using InferHub.Client.Exceptions;
using InferHub.Client.Http;
using InferHub.Client.Models.OpenAi;
using InferHub.Client.Serialization;

namespace InferHub.Client;

/// <inheritdoc cref="IInferHubOpenAiClient"/>
public sealed class InferHubOpenAiClient : IInferHubOpenAiClient
{
    /// <summary>The SSE sentinel that ends a stream. Not JSON, and never deserialized.</summary>
    private const string DoneSentinel = "[DONE]";

    private static InferHubJsonContext Json => InferHubJsonContext.Default;

    private readonly HttpClient httpClient;

    /// <summary>Create a new client. Prefer <c>services.AddInferHubClient(...)</c> in DI.</summary>
    public InferHubOpenAiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    /// <inheritdoc/>
    public Task<ChatCompletionResponse> CreateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        => CreateChatCompletionAsync(request, null, cancellationToken);

    /// <inheritdoc/>
    public async Task<ChatCompletionResponse> CreateChatCompletionAsync(
        ChatCompletionRequest request,
        InferHubCallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Stream = false;

        var (result, response) = await PostAsync("v1/chat/completions", request, Json.ChatCompletionRequest, Json.ChatCompletionResponse, options, cancellationToken);
        using (response)
        {
            result.ServedBy = InferHubHeaders.ReadServedBy(response);
            result.SourceIds = InferHubHeaders.ParseSourceIds(response);
            return result;
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        => StreamChatCompletionAsync(request, null, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        InferHubCallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Stream = true;

        return StreamSseAsync(
            "v1/chat/completions",
            request,
            Json.ChatCompletionRequest,
            Json.ChatCompletionChunk,
            static (chunk, servedBy, sources) =>
            {
                chunk.ServedBy = servedBy;
                chunk.SourceIds = sources;
            },
            options,
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<CompletionResponse> CreateCompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default)
        => CreateCompletionAsync(request, null, cancellationToken);

    /// <inheritdoc/>
    public async Task<CompletionResponse> CreateCompletionAsync(
        CompletionRequest request,
        InferHubCallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Stream = false;

        var (result, response) = await PostAsync("v1/completions", request, Json.CompletionRequest, Json.CompletionResponse, options, cancellationToken);
        using (response)
        {
            result.ServedBy = InferHubHeaders.ReadServedBy(response);
            return result;
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<CompletionResponse> StreamCompletionAsync(CompletionRequest request, CancellationToken cancellationToken = default)
        => StreamCompletionAsync(request, null, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<CompletionResponse> StreamCompletionAsync(
        CompletionRequest request,
        InferHubCallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Stream = true;

        return StreamSseAsync(
            "v1/completions",
            request,
            Json.CompletionRequest,
            Json.CompletionResponse,
            static (chunk, servedBy, _) => chunk.ServedBy = servedBy,
            options,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OpenAiEmbeddingsResponse> CreateEmbeddingsAsync(OpenAiEmbeddingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (result, response) = await PostAsync("v1/embeddings", request, Json.OpenAiEmbeddingsRequest, Json.OpenAiEmbeddingsResponse, options: null, cancellationToken);
        using (response)
        {
            if (result.Data.Count == 0)
            {
                throw new InferHubException(response.StatusCode, "embeddings response had no vectors", string.Empty);
            }

            return result;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OpenAiModel>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("v1/models", cancellationToken);
        await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);

        var list = await response.Content.ReadFromJsonAsync(Json.OpenAiModelList, cancellationToken)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
        return list.Data;
    }

    /// <inheritdoc/>
    public async Task<OpenAiModel?> GetModelAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var response = await httpClient.GetAsync($"v1/models/{Uri.EscapeDataString(id)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(Json.OpenAiModel, cancellationToken)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
    }

    private async Task<(TResult Result, HttpResponseMessage Response)> PostAsync<TRequest, TResult>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestInfo,
        JsonTypeInfo<TResult> resultInfo,
        InferHubCallOptions? options,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, requestInfo)
        };
        InferHubHeaders.Apply(request, options);

        // The response is handed back undisposed: the caller reads its headers for ServedBy and
        // the source ids, then disposes it.
        var response = await httpClient.SendAsync(request, cancellationToken);
        try
        {
            await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync(resultInfo, cancellationToken)
                ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
            return (result, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads a <c>text/event-stream</c> body and stops at the <c>[DONE]</c> sentinel. The line
    /// mechanics — <c>data:</c> accumulation, comments, the blank-line frame boundary — are
    /// <see cref="SseFrameReader"/>'s and are shared with the audio surface; what stays here is
    /// how <em>this</em> dialect ends.
    /// </summary>
    /// <remarks>
    /// Deliberately not the NDJSON loop with a prefix flag. That loop's contract is "one object per
    /// line, stop on <c>done:true</c>"; this one's is "frames until a sentinel", and one method with
    /// a mode switch is two methods sharing a bug — which is also why the reader is split at the
    /// line level and not at the stream level.
    /// <para>
    /// A stream that ends without <c>[DONE]</c> ends without an exception. The hub already sends a
    /// terminal frame with <c>finish_reason: "stop"</c> when a node drops mid-answer; throwing here
    /// would discard the partial answer the caller is holding.
    /// </para>
    /// </remarks>
    private async IAsyncEnumerable<TChunk> StreamSseAsync<TRequest, TChunk>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestInfo,
        JsonTypeInfo<TChunk> chunkInfo,
        Action<TChunk, string?, IReadOnlyList<string>?> decorate,
        InferHubCallOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TChunk : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, requestInfo)
        };
        request.Headers.Accept.ParseAdd("text/event-stream");
        InferHubHeaders.Apply(request, options);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);

        var servedBy = InferHubHeaders.ReadServedBy(response);
        var sources = InferHubHeaders.ParseSourceIds(response);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var frame in SseFrameReader.ReadAsync(stream, cancellationToken))
        {
            // The sentinel ends the stream and is not JSON: deserializing it throws.
            if (frame.Data.Equals(DoneSentinel, StringComparison.Ordinal))
            {
                yield break;
            }

            if (TryParse(frame.Data, chunkInfo, response, out var chunk))
            {
                decorate(chunk, servedBy, sources);
                yield return chunk;
            }
        }
    }

    private static bool TryParse<TChunk>(
        string payload,
        JsonTypeInfo<TChunk> chunkInfo,
        HttpResponseMessage response,
        out TChunk chunk)
        where TChunk : class
    {
        chunk = null!;

        if (payload.Length == 0)
        {
            return false;
        }

        try
        {
            chunk = JsonSerializer.Deserialize(payload, chunkInfo)!;
        }
        catch (JsonException ex)
        {
            throw new InferHubException(response.StatusCode, $"Malformed SSE frame: {ex.Message}", payload);
        }

        return chunk is not null;
    }
}
