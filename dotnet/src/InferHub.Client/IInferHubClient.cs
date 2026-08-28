using InferHub.Client.Models;
using InferHub.Client.Models.Ollama;
using InferHub.Client.Models.Vector;

namespace InferHub.Client;

/// <summary>
/// Client for talking to an InferHub coordinator over its Ollama-compatible HTTP API.
/// Covers blocking + streaming chat/generate, model listing, embeddings, the vector
/// data-plane, status and health.
/// </summary>
public interface IInferHubClient
{
    /// <summary>
    /// List models advertised by the mesh. Wraps <c>GET /api/tags</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TagsResponse> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocking chat call — <c>POST /api/chat</c> with <c>stream:false</c>.
    /// </summary>
    /// <param name="request">Chat request. <see cref="ChatRequest.Stream"/> is forced to <c>false</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocking chat call with per-call RAG/routing options — <c>POST /api/chat</c> with
    /// <c>stream:false</c>. <paramref name="options"/> map to <c>X-InferHub-*</c> headers; when
    /// retrieval is requested the returned <see cref="ChatResponse.SourceIds"/> carries the
    /// grounding record ids from <c>X-InferHub-Sources</c>. A <c>424 Failed Dependency</c>
    /// (retrieval unavailable / <c>OnMissing=error</c>) surfaces as
    /// <see cref="Exceptions.InferHubRetrievalException"/>.
    /// </summary>
    /// <param name="request">Chat request. <see cref="ChatRequest.Stream"/> is forced to <c>false</c>.</param>
    /// <param name="options">Per-call retrieval and conversation options; <c>null</c> for a plain call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ChatResponse> ChatAsync(ChatRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocking generate call — <c>POST /api/generate</c> with <c>stream:false</c>.
    /// </summary>
    /// <param name="request">Generate request. <see cref="GenerateRequest.Stream"/> is forced to <c>false</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<GenerateResponse> GenerateAsync(GenerateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocking generate call with per-call RAG/routing options — <c>POST /api/generate</c> with
    /// <c>stream:false</c>. <paramref name="options"/> map to <c>X-InferHub-*</c> headers; when
    /// retrieval is requested the returned <see cref="GenerateResponse.SourceIds"/> carries the
    /// grounding record ids from <c>X-InferHub-Sources</c>. A <c>424 Failed Dependency</c>
    /// surfaces as <see cref="Exceptions.InferHubRetrievalException"/>.
    /// </summary>
    /// <param name="request">Generate request. <see cref="GenerateRequest.Stream"/> is forced to <c>false</c>.</param>
    /// <param name="options">Per-call retrieval and conversation options; <c>null</c> for a plain call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<GenerateResponse> GenerateAsync(GenerateRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming chat call — <c>POST /api/chat</c> with <c>stream:true</c>. Yields one
    /// <see cref="ChatResponse"/> per NDJSON chunk; the enumerator stops after the chunk
    /// with <c>done:true</c>. A terminal error chunk (<c>{ "error": …, "done": true }</c>)
    /// is surfaced as an <see cref="Exceptions.InferHubException"/> instead of a silent stop.
    /// </summary>
    /// <param name="request">Chat request. <see cref="ChatRequest.Stream"/> is forced to <c>true</c>.</param>
    /// <param name="cancellationToken">Cancels the read loop; a cancelled token throws promptly.</param>
    IAsyncEnumerable<ChatResponse> ChatStreamAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming chat call with per-call RAG/routing options — <c>POST /api/chat</c> with
    /// <c>stream:true</c>. <paramref name="options"/> map to <c>X-InferHub-*</c> headers; when
    /// retrieval is requested every yielded <see cref="ChatResponse"/> carries the same
    /// <see cref="ChatResponse.SourceIds"/> (read from <c>X-InferHub-Sources</c> once, before the
    /// first chunk). A <c>424 Failed Dependency</c> surfaces as
    /// <see cref="Exceptions.InferHubRetrievalException"/>.
    /// </summary>
    /// <param name="request">Chat request. <see cref="ChatRequest.Stream"/> is forced to <c>true</c>.</param>
    /// <param name="options">Per-call retrieval and conversation options; <c>null</c> for a plain call.</param>
    /// <param name="cancellationToken">Cancels the read loop; a cancelled token throws promptly.</param>
    IAsyncEnumerable<ChatResponse> ChatStreamAsync(ChatRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming generate call — <c>POST /api/generate</c> with <c>stream:true</c>. Yields
    /// one <see cref="GenerateResponse"/> per NDJSON chunk; stops after <c>done:true</c>.
    /// A terminal error chunk is surfaced as an <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="request">Generate request. <see cref="GenerateRequest.Stream"/> is forced to <c>true</c>.</param>
    /// <param name="cancellationToken">Cancels the read loop; a cancelled token throws promptly.</param>
    IAsyncEnumerable<GenerateResponse> GenerateStreamAsync(GenerateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming generate call with per-call RAG/routing options — <c>POST /api/generate</c> with
    /// <c>stream:true</c>. <paramref name="options"/> map to <c>X-InferHub-*</c> headers; when
    /// retrieval is requested every yielded <see cref="GenerateResponse"/> carries the same
    /// <see cref="GenerateResponse.SourceIds"/> (read from <c>X-InferHub-Sources</c> once, before
    /// the first chunk). A <c>424 Failed Dependency</c> surfaces as
    /// <see cref="Exceptions.InferHubRetrievalException"/>.
    /// </summary>
    /// <param name="request">Generate request. <see cref="GenerateRequest.Stream"/> is forced to <c>true</c>.</param>
    /// <param name="options">Per-call retrieval and conversation options; <c>null</c> for a plain call.</param>
    /// <param name="cancellationToken">Cancels the read loop; a cancelled token throws promptly.</param>
    IAsyncEnumerable<GenerateResponse> GenerateStreamAsync(GenerateRequest request, InferHubCallOptions? options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch embeddings call — <c>POST /api/embed</c>. Accepts a single string or a
    /// string array as <see cref="EmbedRequest.Input"/>; returns one vector per input
    /// in <see cref="EmbedResponse.Embeddings"/>. Use
    /// <see cref="EmbedRequest.FromText(string, string)"/> or
    /// <see cref="EmbedRequest.FromTexts(string, IEnumerable{string})"/> for the common cases.
    /// Missing embedding node → <c>404</c>, bad body → <c>400</c>, node dropped mid-flight → <c>502</c>,
    /// all surfaced as <see cref="Exceptions.InferHubException"/>. An empty vector list on 200
    /// is treated as a malformed response and thrown, never silently returned.
    /// </summary>
    /// <param name="request">Embed request. <see cref="EmbedRequest.Model"/> is required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EmbedResponse> EmbedAsync(EmbedRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy single-input embeddings call — <c>POST /api/embeddings</c>. Prefer
    /// <see cref="EmbedAsync"/> for new code; this exists for drop-in Ollama callers.
    /// Returns one vector in <see cref="EmbeddingsResponse.Embedding"/>.
    /// </summary>
    /// <param name="request">Legacy embeddings request. <see cref="EmbeddingsRequest.Model"/> and
    /// <see cref="EmbeddingsRequest.Prompt"/> are required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EmbeddingsResponse> EmbedLegacyAsync(EmbeddingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert a record into a collection — <c>POST /api/vector/{collection}/upsert</c>. Supply a
    /// raw vector (<see cref="VectorUpsert.FromVector"/>) or text to embed on a node
    /// (<see cref="VectorUpsert.FromText"/>). An existing id is overwritten. Unknown collection
    /// or no embedding node → <c>404</c>, missing vector/text → <c>400</c>, all surfaced as
    /// <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="collection">Target collection name.</param>
    /// <param name="upsert">Record to write. <see cref="VectorUpsert.Id"/> is required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<VectorRecord> UpsertAsync(string collection, VectorUpsert upsert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nearest-neighbour search — <c>POST /api/vector/{collection}/query</c>. Search by a raw
    /// vector or by text to embed (<see cref="VectorQuery.FromVector"/> / <see cref="VectorQuery.FromText"/>).
    /// Returns up to <see cref="VectorQuery.K"/> ranked <see cref="VectorMatch"/> values, closest first.
    /// Unknown collection → <c>404</c>.
    /// </summary>
    /// <param name="collection">Collection to search.</param>
    /// <param name="query">Query body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<VectorMatch>> QueryAsync(string collection, VectorQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// RAG-convenience read — <c>POST /api/vector/{collection}/retrieve</c>. Same body and result
    /// shape as <see cref="QueryAsync"/>; exists as the retrieval-oriented name callers reach for
    /// when grounding a prompt.
    /// </summary>
    /// <param name="collection">Collection to search.</param>
    /// <param name="query">Query body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<VectorMatch>> RetrieveAsync(string collection, VectorQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch a single record by id — <c>GET /api/vector/{collection}/{id}</c>. Returns <c>null</c>
    /// when the record (or collection) is not found; other failures surface as
    /// <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="id">Record id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<VectorRecord?> GetRecordAsync(string collection, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a record by id — <c>DELETE /api/vector/{collection}/{id}</c>. Returns <c>true</c> when
    /// a record was removed, <c>false</c> when it did not exist; other failures surface as
    /// <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="id">Record id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> DeleteRecordAsync(string collection, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Coordinator/fleet snapshot — <c>GET /api/status</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<StatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cheap liveness probe — <c>GET /health</c>. Returns <c>true</c> on 2xx, <c>false</c> otherwise.
    /// Never throws for non-success statuses — throws only on transport errors.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}
