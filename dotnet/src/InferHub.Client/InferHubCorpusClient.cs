using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InferHub.Client.Configuration;
using InferHub.Client.Exceptions;
using InferHub.Client.Http;
using InferHub.Client.Models.Corpus;
using InferHub.Client.Serialization;

namespace InferHub.Client;

/// <inheritdoc cref="IInferHubCorpusClient"/>
public sealed class InferHubCorpusClient : IInferHubCorpusClient
{
    private static InferHubJsonContext Json => InferHubJsonContext.Default;

    private readonly HttpClient httpClient;
    private readonly TimeSpan requestTimeout;

    /// <summary>
    /// Create a new client. Prefer <c>services.AddInferHubClient(...)</c> in DI, which registers
    /// this client with an infinite <see cref="HttpClient.Timeout"/> — an ingest is chunked and
    /// embedded on the fleet before it answers, so a 200-page PDF is not a 100-second call — and
    /// applies <see cref="InferHubClientOptions.Timeout"/> per call instead.
    /// </summary>
    /// <param name="httpClient">Transport. Set <c>Timeout = Timeout.InfiniteTimeSpan</c> when constructing this by hand.</param>
    /// <param name="options">Client options; <c>null</c> means no per-call timeout.</param>
    public InferHubCorpusClient(HttpClient httpClient, InferHubClientOptions? options = null)
    {
        this.httpClient = httpClient;
        requestTimeout = options?.Timeout ?? Timeout.InfiniteTimeSpan;
    }

    // ---- ingestion -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<IngestResult> IngestTextAsync(string collection, TextDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            throw new ArgumentException($"{nameof(TextDocument.Text)} is required.", nameof(document));
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, DocumentsPath(collection))
        {
            Content = JsonContent.Create(document, Json.TextDocument)
        };

        return await SendForIngestAsync(message, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IngestResult> IngestFileAsync(string collection, FileDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(document);

        using var content = BuildUploadForm(document);
        using var message = new HttpRequestMessage(HttpMethod.Post, DocumentsPath(collection)) { Content = content };

        return await SendForIngestAsync(message, cancellationToken);
    }

    // ---- the documents in a collection ------------------------------------------------------

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(string collection, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        using var message = new HttpRequestMessage(HttpMethod.Get, DocumentsPath(collection));
        var answer = await SendForJsonAsync(message, Json.DocumentsResponse, cancellationToken);
        return answer.Documents;
    }

    /// <inheritdoc/>
    public async Task<DocumentSummary?> GetDocumentAsync(string collection, string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        using var message = new HttpRequestMessage(HttpMethod.Get, DocumentPath(collection, documentId));
        return await SendForOptionalJsonAsync(message, Json.DocumentSummary, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(string collection, string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        using var message = new HttpRequestMessage(HttpMethod.Get, $"{DocumentPath(collection, documentId)}/chunks");
        var answer = await SendForOptionalJsonAsync(message, Json.ChunksResponse, cancellationToken);
        return answer?.Chunks ?? Array.Empty<DocumentChunk>();
    }

    /// <inheritdoc/>
    public async Task<DocumentDeletion?> DeleteDocumentAsync(string collection, string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        using var message = new HttpRequestMessage(HttpMethod.Delete, DocumentPath(collection, documentId));
        return await SendForOptionalJsonAsync(message, Json.DocumentDeletion, cancellationToken);
    }

    // ---- search ------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<SearchResponse> SearchAsync(string collection, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException($"{nameof(SearchRequest.Query)} is required.", nameof(request));
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/collections/{Escape(collection)}/search")
        {
            Content = JsonContent.Create(request, Json.SearchRequest)
        };

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var response = await SendAsync(message, token);

            var answer = await response.Content.ReadFromJsonAsync(Json.SearchResponse, token)
                ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);

            answer.ServedBy = InferHubHeaders.ReadServedBy(response);
            return answer;
        });
    }

    /// <inheritdoc/>
    public Task<SearchResponse> SearchAsync(string collection, string query, CancellationToken cancellationToken = default)
        => SearchAsync(collection, new SearchRequest(query), cancellationToken);

    // ---- plumbing ----------------------------------------------------------------------------

    /// <summary>
    /// An upload's multipart body, with <b>every field written before the file part</b>.
    /// </summary>
    /// <remarks>
    /// Above the hub's <c>Tools:MaxStreamedBytes</c> the request is routed from its leading fields
    /// while the bytes are still arriving, so a field after the file is refused with a <c>400</c>
    /// naming it. The buffered path below that ceiling tolerates any order, which is what makes the
    /// mistake dangerous: correct on every small test file, wrong on the first real one.
    /// </remarks>
    private static MultipartFormDataContent BuildUploadForm(FileDocument document)
    {
        if (document.Content is null)
        {
            throw new ArgumentException($"{nameof(FileDocument.Content)} is required.", nameof(document));
        }

        if (string.IsNullOrWhiteSpace(document.FileName))
        {
            throw new ArgumentException(
                $"{nameof(FileDocument.FileName)} is required: the hub resolves the extractor from its extension.",
                nameof(document));
        }

        var content = new MultipartFormDataContent();

        try
        {
            if (!string.IsNullOrWhiteSpace(document.Id))
            {
                content.Add(new StringContent(document.Id), "id");
            }

            if (document.Metadata is { Count: > 0 } metadata)
            {
                var json = JsonSerializer.Serialize(
                    new Dictionary<string, string>(metadata),
                    Json.DictionaryStringString);

                content.Add(new StringContent(json, Encoding.UTF8, "application/json"), "metadata");
            }

            if (!string.IsNullOrWhiteSpace(document.Model))
            {
                content.Add(new StringContent(document.Model), "model");
            }

            // Last, always. See the remarks above.
            var file = new StreamContent(document.Content);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(
                string.IsNullOrWhiteSpace(document.ContentType) ? "application/octet-stream" : document.ContentType);

            // The file name is sent here, unlike on an image upload: the hub resolves the extractor
            // from its extension, stores it as each chunk's source, and falls back to it for the
            // document id. Dropping it would make every text upload an unreadable octet-stream.
            content.Add(file, "file", document.FileName);

            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Send an ingest and read its result, <b>including the <c>partial</c> one that arrives as a
    /// <c>500</c></b>.
    /// </summary>
    /// <remarks>
    /// The hub answers a partial ingest with an error status on purpose — a half-ingested document
    /// that claims success is worse than a failure — but the body is a complete
    /// <see cref="IngestResult"/> naming a document that really is in the store. Throwing it away
    /// because the status code is a <c>5xx</c> would leave a caller unable to resume. Any other
    /// <c>500</c>, and any body that is not a partial result, throws as usual.
    /// </remarks>
    private async Task<IngestResult> SendForIngestAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var response = await httpClient.SendAsync(message, token);

            if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                var body = await response.Content.ReadAsStringAsync(token);

                if (TryReadPartial(body) is { } partial)
                {
                    return partial;
                }

                throw new InferHubException(
                    response.StatusCode,
                    InferHubResponse.TryExtractErrorMessage(body) ?? "InferHub ingest failed with status 500.",
                    body);
            }

            await InferHubResponse.EnsureSuccessAsync(response, token);

            return await response.Content.ReadFromJsonAsync(Json.IngestResult, token)
                ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
        });
    }

    private static IngestResult? TryReadPartial(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize(body, Json.IngestResult);
            return result is { } parsed && parsed.IsPartial ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<T> SendForJsonAsync<T>(
        HttpRequestMessage message,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var response = await SendAsync(message, token);

            return await response.Content.ReadFromJsonAsync(typeInfo, token)
                ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
        });
    }

    /// <summary>
    /// A read that addresses one document, where <c>404</c> is the answer rather than a failure.
    /// </summary>
    /// <remarks>
    /// The hub cannot tell a caller much more here: a missing collection and a missing document both
    /// come back as a <c>404</c> naming what was not found, and either way this document is not
    /// available. Search is the deliberate exception and throws.
    /// </remarks>
    private async Task<T?> SendForOptionalJsonAsync<T>(
        HttpRequestMessage message,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var response = await httpClient.SendAsync(message, token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await InferHubResponse.EnsureSuccessAsync(response, token);

            return await response.Content.ReadFromJsonAsync(typeInfo, token)
                ?? throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
        });
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
            throw new TimeoutException($"The InferHub corpus request timed out after {requestTimeout.TotalSeconds:0.#}s.");
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

    private static string DocumentsPath(string collection)
        => $"api/collections/{Escape(collection)}/documents";

    private static string DocumentPath(string collection, string documentId)
        => $"api/collections/{Escape(collection)}/documents/{Escape(documentId)}";

    private static string Escape(string segment) => Uri.EscapeDataString(segment);
}
