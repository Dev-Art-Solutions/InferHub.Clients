using InferHub.Client.Exceptions;
using InferHub.Client.Models.Corpus;

namespace InferHub.Client;

/// <summary>
/// Documents in, chunks out, and the search that reads them back —
/// <c>/api/collections/{collection}/documents</c> and <c>/api/collections/{collection}/search</c>.
/// A hub and a standalone node both serve every method here, at the same paths with the same bodies.
/// </summary>
/// <remarks>
/// <para>
/// Guarded by the <b>client</b> key, not the admin key: ingesting is a client action, and requiring
/// an admin key for it would push a deployment toward using one key for everything. Creating and
/// dropping collections is still admin work — <see cref="IInferHubAdminClient"/> — except that a
/// client whose key carries a collection scope provisions names inside that scope by ingesting
/// into them.
/// </para>
/// <para>
/// <b>The hub does not keep the file.</b> Chunk text, a content hash and metadata; not the
/// document. Nor does this client keep a local record of what it uploaded.
/// </para>
/// </remarks>
public interface IInferHubCorpusClient
{
    /// <summary>
    /// Upload a document as text — <c>POST /api/collections/{collection}/documents</c> with a JSON
    /// body.
    /// </summary>
    /// <param name="collection">Collection to ingest into.</param>
    /// <param name="document">The text, its id and its metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// What the ingest did. <b>A <see cref="IngestStatuses.Partial"/> result is returned, not
    /// thrown</b>, even though it arrives with HTTP <c>500</c> — see <see cref="IngestResult"/>.
    /// </returns>
    /// <exception cref="InferHubException">
    /// The upload was refused: <c>404</c> (no such collection, or no node holds the embedding
    /// model), <c>413</c> (too large), <c>415</c> (a format the hub does not read), <c>422</c> (a
    /// format it reads that yielded no usable text — a scanned PDF is rejected rather than
    /// half-ingested), or <c>503</c> (the node owning this collection is away).
    /// </exception>
    Task<IngestResult> IngestTextAsync(string collection, TextDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload a document as a file — <c>POST /api/collections/{collection}/documents</c> as
    /// multipart. The stream is sent as it is read; nothing is buffered into an array.
    /// </summary>
    /// <param name="collection">Collection to ingest into.</param>
    /// <param name="document">The stream, its file name and its metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the ingest did; a partial result is returned rather than thrown.</returns>
    /// <exception cref="InferHubException">
    /// As <see cref="IngestTextAsync"/>. <c>415</c> additionally covers a PDF sent to a standalone
    /// node or a node-owned collection: the PDF extractor ships with the coordinator only, and the
    /// message says so rather than naming a missing service.
    /// </exception>
    Task<IngestResult> IngestFileAsync(string collection, FileDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// List a collection's documents — <c>GET /api/collections/{collection}/documents</c>.
    /// </summary>
    /// <param name="collection">Collection to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The documents, or an empty list for a collection holding none.</returns>
    /// <exception cref="InferHubException">The collection does not exist (<c>404</c>).</exception>
    Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch one document's summary — <c>GET /api/collections/{collection}/documents/{id}</c>.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="documentId">Document id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>null</c> when there is no such document — an absence, not an error.</returns>
    Task<DocumentSummary?> GetDocumentAsync(string collection, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read a document's chunks in order —
    /// <c>GET /api/collections/{collection}/documents/{id}/chunks</c>. What the corpus will actually
    /// retrieve, which is the thing to look at when a collection is retrieving badly.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="documentId">Document id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chunks, or an empty list when there is no such document.</returns>
    Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(string collection, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a document and every chunk of it —
    /// <c>DELETE /api/collections/{collection}/documents/{id}</c>.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="documentId">Document id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was removed, or <c>null</c> when there was no such document.</returns>
    Task<DocumentDeletion?> DeleteDocumentAsync(string collection, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run a query against a collection — <c>POST /api/collections/{collection}/search</c>.
    /// </summary>
    /// <param name="collection">Collection to search.</param>
    /// <param name="request">The query, and the mode, k and rerank knobs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The hits <b>in the hub's own order</b>. Empty when nothing matched, which is a real answer.
    /// </returns>
    /// <exception cref="InferHubRetrievalException">
    /// Retrieval was asked for and could not run — <c>424</c>, typically no node holding the
    /// embedding model.
    /// </exception>
    /// <exception cref="InferHubException">
    /// The collection does not exist (<c>404</c>). <b>Unlike a missing document, this throws</b>: a
    /// name with a typo in it that answered "no hits" is how a retrieval system tells its owner
    /// their corpus is fine when it is empty.
    /// </exception>
    Task<SearchResponse> SearchAsync(string collection, SearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Search with everything but the query left at the deployment's defaults.</summary>
    /// <param name="collection">Collection to search.</param>
    /// <param name="query">What to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hits, in the hub's own order.</returns>
    Task<SearchResponse> SearchAsync(string collection, string query, CancellationToken cancellationToken = default);
}
