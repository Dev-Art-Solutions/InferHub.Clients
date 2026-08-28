using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using InferHub.Client.Exceptions;
using InferHub.Client.Models;
using InferHub.Client.Models.Ollama;
using InferHub.Client.Models.Vector;
using InferHub.Client.Serialization;

namespace InferHub.Client;

/// <inheritdoc cref="IInferHubClient"/>
public sealed class InferHubClient : IInferHubClient
{
    private static InferHubJsonContext Json => InferHubJsonContext.Default;

    private readonly HttpClient httpClient;

    /// <summary>Create a new client. Prefer <c>services.AddInferHubClient(...)</c> in DI.</summary>
    public InferHubClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<TagsResponse> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync("api/tags", Json.TagsResponse, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => ChatAsync(request, null, cancellationToken);

    /// <inheritdoc/>
    public async Task<ChatResponse> ChatAsync(ChatRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default)
    {
        request.Stream = false;
        return await PostForResultAsync(
            "api/chat", request, Json.ChatRequest, Json.ChatResponse, options,
            static (r, sources) => r.SourceIds = sources,
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<GenerateResponse> GenerateAsync(GenerateRequest request, CancellationToken cancellationToken = default)
        => GenerateAsync(request, null, cancellationToken);

    /// <inheritdoc/>
    public async Task<GenerateResponse> GenerateAsync(GenerateRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default)
    {
        request.Stream = false;
        return await PostForResultAsync(
            "api/generate", request, Json.GenerateRequest, Json.GenerateResponse, options,
            static (r, sources) => r.SourceIds = sources,
            cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatResponse> ChatStreamAsync(ChatRequest request, CancellationToken cancellationToken = default)
        => ChatStreamAsync(request, null, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatResponse> ChatStreamAsync(ChatRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default)
    {
        request.Stream = true;
        return StreamNdjsonAsync(
            "api/chat",
            request,
            Json.ChatRequest,
            Json.ChatResponse,
            static chunk => (chunk.Done == true, chunk.Error),
            static (chunk, sources) => chunk.SourceIds = sources,
            options,
            cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<GenerateResponse> GenerateStreamAsync(GenerateRequest request, CancellationToken cancellationToken = default)
        => GenerateStreamAsync(request, null, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<GenerateResponse> GenerateStreamAsync(GenerateRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default)
    {
        request.Stream = true;
        return StreamNdjsonAsync(
            "api/generate",
            request,
            Json.GenerateRequest,
            Json.GenerateResponse,
            static chunk => (chunk.Done == true, chunk.Error),
            static (chunk, sources) => chunk.SourceIds = sources,
            options,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<EmbedResponse> EmbedAsync(EmbedRequest request, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync("api/embed", request, Json.EmbedRequest, Json.EmbedResponse, cancellationToken);
        if (response.Embeddings is null || response.Embeddings.Length == 0)
        {
            throw new InferHubException(System.Net.HttpStatusCode.OK, "embed response had no vectors", string.Empty);
        }

        return response;
    }

    /// <inheritdoc/>
    public async Task<EmbeddingsResponse> EmbedLegacyAsync(EmbeddingsRequest request, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync("api/embeddings", request, Json.EmbeddingsRequest, Json.EmbeddingsResponse, cancellationToken);
        if (response.Embedding is null || response.Embedding.Length == 0)
        {
            throw new InferHubException(System.Net.HttpStatusCode.OK, "embeddings response had no vector", string.Empty);
        }

        return response;
    }

    /// <inheritdoc/>
    public async Task<VectorRecord> UpsertAsync(string collection, VectorUpsert upsert, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(upsert);
        return await PostAsync($"api/vector/{Escape(collection)}/upsert", upsert, Json.VectorUpsert, Json.VectorRecord, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorMatch>> QueryAsync(string collection, VectorQuery query, CancellationToken cancellationToken = default)
    {
        return await SearchAsync(collection, "query", query, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorMatch>> RetrieveAsync(string collection, VectorQuery query, CancellationToken cancellationToken = default)
    {
        return await SearchAsync(collection, "retrieve", query, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<VectorRecord?> GetRecordAsync(string collection, string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var response = await httpClient.GetAsync($"api/vector/{Escape(collection)}/{Escape(id)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(Json.VectorRecord, cancellationToken)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteRecordAsync(string collection, string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var response = await httpClient.DeleteAsync($"api/vector/{Escape(collection)}/{Escape(id)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    /// <inheritdoc/>
    public async Task<StatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync("api/status", Json.StatusResponse, cancellationToken);
    }

    private async Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, string action, VectorQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(query);
        var envelope = await PostAsync($"api/vector/{Escape(collection)}/{action}", query, Json.VectorQuery, Json.VectorMatchesResponse, cancellationToken);
        return envelope.Matches;
    }

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    /// <inheritdoc/>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("health", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<TResult> GetAsync<TResult>(string path, JsonTypeInfo<TResult> resultInfo, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync(resultInfo, cancellationToken);
        return result ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
    }

    private async Task<TResult> PostAsync<TRequest, TResult>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestInfo,
        JsonTypeInfo<TResult> resultInfo,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(path, body, requestInfo, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync(resultInfo, cancellationToken);
        return result ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
    }

    private async Task<TResult> PostForResultAsync<TRequest, TResult>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestInfo,
        JsonTypeInfo<TResult> resultInfo,
        InferHubCallOptions? options,
        Action<TResult, IReadOnlyList<string>?> setSources,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, requestInfo)
        };
        ApplyCallHeaders(request, options);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync(resultInfo, cancellationToken)
            ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
        setSources(result, ParseSourceIds(response));
        return result;
    }

    private async IAsyncEnumerable<TChunk> StreamNdjsonAsync<TRequest, TChunk>(
        string path,
        TRequest body,
        JsonTypeInfo<TRequest> requestInfo,
        JsonTypeInfo<TChunk> chunkInfo,
        Func<TChunk, (bool Done, string? Error)> inspect,
        Action<TChunk, IReadOnlyList<string>?> setSources,
        InferHubCallOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TChunk : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, requestInfo)
        };
        ApplyCallHeaders(request, options);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var sources = ParseSourceIds(response);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            TChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(line, chunkInfo);
            }
            catch (JsonException ex)
            {
                throw new InferHubException(response.StatusCode, $"Malformed NDJSON chunk: {ex.Message}", line);
            }

            if (chunk is null)
            {
                continue;
            }

            var (done, error) = inspect(chunk);
            if (!string.IsNullOrEmpty(error))
            {
                throw new InferHubException(response.StatusCode, error, line);
            }

            setSources(chunk, sources);
            yield return chunk;

            if (done)
            {
                yield break;
            }
        }
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => Http.InferHubResponse.EnsureSuccessAsync(response, cancellationToken);

    private static void ApplyCallHeaders(HttpRequestMessage request, InferHubCallOptions? options)
    {
        if (options is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.ConversationId))
        {
            request.Headers.TryAddWithoutValidation("X-InferHub-Conversation", options.ConversationId);
        }

        var retrieval = options.Retrieval;
        if (retrieval is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(retrieval.Collection))
        {
            throw new ArgumentException("RetrievalOptions.Collection is required.", nameof(options));
        }

        request.Headers.TryAddWithoutValidation("X-InferHub-Retrieve", retrieval.Collection);

        if (retrieval.K is int k)
        {
            request.Headers.TryAddWithoutValidation("X-InferHub-Retrieve-K", k.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(retrieval.Model))
        {
            request.Headers.TryAddWithoutValidation("X-InferHub-Retrieve-Model", retrieval.Model);
        }
    }

    private static IReadOnlyList<string>? ParseSourceIds(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-InferHub-Sources", out var values))
        {
            return null;
        }

        var raw = string.Concat(values).Trim();
        if (raw.Length == 0)
        {
            return Array.Empty<string>();
        }

        // The coordinator echoes a JSON array: X-InferHub-Sources: ["id", "id2"].
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var ids = new List<string>(doc.RootElement.GetArrayLength());
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var id = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
                    if (!string.IsNullOrEmpty(id))
                    {
                        ids.Add(id);
                    }
                }

                return ids;
            }
        }
        catch (JsonException)
        {
            // Not a JSON array — fall back to a comma-separated list.
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

}
