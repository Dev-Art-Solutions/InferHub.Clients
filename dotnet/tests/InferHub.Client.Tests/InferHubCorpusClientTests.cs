using System.Net;
using System.Text;
using InferHub.Client.Exceptions;
using InferHub.Client.Models.Corpus;

namespace InferHub.Client.Tests;

/// <summary>
/// The corpus surface — <c>/api/collections/{c}/documents</c> and <c>/search</c>, phase 12.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every payload in this file was recorded from a live InferHub 3.37.0</b> on 2026-09-02, by
/// driving the routes with curl and pasting what came back, unicode escapes and all. The target was
/// a <b>standalone node in solo mode</b> with <c>LocalApi:Retrieval:Enabled=true</c> on
/// <c>:5091</c>, embedding with <c>nomic-embed-text:latest</c> and reranking with
/// <c>llama3.1:latest</c>, because the always-on hub on <c>:5080</c> runs with
/// <c>VectorStore:Enabled=false</c> — where these routes are not mapped at all and answer a
/// <b>404 with an empty body</b>.
/// </para>
/// <para>
/// That target is not a weaker recording than the hub would have been: ingestion, chunking, the
/// document index and the search pipeline all live in <c>InferHub.Shared</c> and the node runs the
/// coordinator's own code. Two differences are real and both are recorded below — <b>PDF is a
/// <c>415</c> on a node</b> (the extractor ships with the coordinator), and the node writes
/// <c>"error":null</c> where the coordinator omits the field.
/// </para>
/// <para>
/// <b>One shape is derived and marked:</b> a partial ingest in which <em>some</em> batches
/// succeeded. The recorded partial below is the all-batches-failed case, which is what a missing
/// embedding model produces on demand; a mixed one needs a fleet that fails halfway through a
/// document. Phase 25 is where that arrives.
/// </para>
/// </remarks>
public class InferHubCorpusClientTests
{
    // ---- recorded: ingestion ----------------------------------------------------------------

    private const string RecordedIngested = """
        {"documentId":"onboarding","collection":"handbook","status":"ingested","chunks":1,"chunksEmbedded":1,"bytes":149,"contentHash":"6dd690d5445a82378e2a036c882a08588503c5c2b4ce45a5ff8d6df24b1d408b","error":null}
        """;

    // The same bytes posted twice. chunksEmbedded drops to 0 while chunks stays 1: nothing was
    // re-embedded, and the document is still there.
    private const string RecordedUnchanged = """
        {"documentId":"onboarding","collection":"handbook","status":"unchanged","chunks":1,"chunksEmbedded":0,"bytes":149,"contentHash":"6dd690d5445a82378e2a036c882a08588503c5c2b4ce45a5ff8d6df24b1d408b","error":null}
        """;

    // THE PAYLOAD THIS PHASE TURNS ON, recorded by naming an embedding model no node advertises:
    // HTTP 500, and a complete body. A client that maps every 5xx onto an exception throws away
    // the document id, the chunk count and the sentence saying what to fix.
    private const string RecordedPartial = """
        {"documentId":"z","collection":"handbook","status":"partial","chunks":1,"chunksEmbedded":0,"bytes":11,"contentHash":"12998c017066eb0d2a70b94e6ed3192985855ce390f321bbdb832022888bd251","error":"no node is advertising embedding model 'no-such-embed-model'"}
        """;

    private const string RecordedPdfOnNode = """
        {"error":"PDF ingestion is not available on a standalone node: the PDF text extractor ships with the coordinator only. Convert the document to text or Markdown first, or ingest it into a hub."}
        """;

    private const string RecordedUnsupportedType = """
        {"error":"unsupported document type 'application/octet-stream'; supported: text/plain, text/markdown, text/html, application/json, application/pdf"}
        """;

    // ---- recorded: the documents in a collection ---------------------------------------------

    private const string RecordedDocumentList = """
        {"collection":"handbook","documents":[{"id":"architecture","chunks":1,"bytes":149,"contentHash":"086b599c926627a7c70bbf308debda9d8ded56a315dbdf120663bf55eab98392","ingestedAt":"2026-09-02T20:49:45.6148809+00:00","source":"notes.md","mediaType":"text/markdown","status":"complete"},{"id":"onboarding","chunks":1,"bytes":149,"contentHash":"6dd690d5445a82378e2a036c882a08588503c5c2b4ce45a5ff8d6df24b1d408b","ingestedAt":"2026-09-02T20:49:02.7200377+00:00","source":"handbook.md","mediaType":"text/markdown","status":"complete"}]}
        """;

    private const string RecordedDocument = """
        {"id":"onboarding","chunks":1,"bytes":149,"contentHash":"6dd690d5445a82378e2a036c882a08588503c5c2b4ce45a5ff8d6df24b1d408b","ingestedAt":"2026-09-02T20:49:02.7200377+00:00","source":"handbook.md","mediaType":"text/markdown","status":"complete"}
        """;

    // Note "index":"0" — a STRING. It is chunk metadata and the hub stores chunk metadata as a
    // string map, so this route hands the number back as it was stored. A search hit's page on the
    // same corpus is a real int.
    private const string RecordedChunks = """
        {"collection":"handbook","documentId":"onboarding","chunks":[{"id":"96117a8f28a5027b1479bae903b56f7bcee5970ebfe81d6bec6806f0fc3c3a3f","index":"0","page":null,"text":"Error E-4021 means the coordinator refused a request because no node holds the model. Wait for a node to join, or pull the model on an existing node."}]}
        """;

    private const string RecordedDeleted = """
        {"collection":"handbook","documentId":"policy.txt","deleted":true,"chunks":1}
        """;

    private const string RecordedDocumentNotFound = """
        {"error":"document 'nope' not found in 'handbook'"}
        """;

    private const string RecordedCollectionNotFound = """
        {"error":"collection 'nope' does not exist"}
        """;

    // ---- recorded: search ---------------------------------------------------------------------

    private const string RecordedVectorSearch = """
        {"collection":"handbook","mode":"vector","hits":[{"id":"96117a8f28a5027b1479bae903b56f7bcee5970ebfe81d6bec6806f0fc3c3a3f","score":0.6114709941765204,"documentId":"onboarding","text":"Error E-4021 means the coordinator refused a request because no node holds the model. Wait for a node to join, or pull the model on an existing node."}]}
        """;

    // Hybrid, un-reranked: descending score, which is what makes the next one legible.
    private const string RecordedHybridSearch = """
        {"collection":"handbook","mode":"hybrid","hits":[{"id":"96117a8f28a5027b1479bae903b56f7bcee5970ebfe81d6bec6806f0fc3c3a3f","score":0.03252247488101534,"documentId":"onboarding","text":"Error E-4021 means the coordinator refused a request because no node holds the model. Wait for a node to join, or pull the model on an existing node."},{"id":"9e99656a7441cc1d2714af840b40357c2cd773946e9e13740879652ca61735aa","score":0.01639344262295082,"documentId":"policy.txt","text":"Payroll runs on the fifth working day. Expenses over 500 EUR need approval from a line manager."},{"id":"a7d9917c40a0b45ed1b3493f6afb9e852c1a690d9cca3f6bc35677bcc31e895a","score":0.015873015873015872,"documentId":"architecture","text":"A node reaches the coordinator over SignalR and never accepts inbound connections.\n\nThe vector store rebuilds its index at startup from the raw log."}]}
        """;

    // THE SAME QUERY WITH rerank:true, recorded from the same corpus. The expenses chunk is first
    // and carries the LOWER score (0.0164 against 0.0325): a rerank reorders the list and leaves
    // every score as retrieval computed it. Sorting these by score puts the E-4021 chunk on top of
    // an answer about expense approval — which is why the client returns them in wire order.
    private const string RecordedRerankedSearch = """
        {"collection":"handbook","mode":"hybrid","hits":[{"id":"9e99656a7441cc1d2714af840b40357c2cd773946e9e13740879652ca61735aa","score":0.01639344262295082,"documentId":"policy.txt","text":"Payroll runs on the fifth working day. Expenses over 500 EUR need approval from a line manager."},{"id":"96117a8f28a5027b1479bae903b56f7bcee5970ebfe81d6bec6806f0fc3c3a3f","score":0.03252247488101534,"documentId":"onboarding","text":"Error E-4021 means the coordinator refused a request because no node holds the model. Wait for a node to join, or pull the model on an existing node."},{"id":"a7d9917c40a0b45ed1b3493f6afb9e852c1a690d9cca3f6bc35677bcc31e895a","score":0.015873015873015872,"documentId":"architecture","text":"A node reaches the coordinator over SignalR and never accepts inbound connections.\n\nThe vector store rebuilds its index at startup from the raw log."}]}
        """;

    // 424, not 404: retrieval was asked for and could not run.
    private const string RecordedRetrievalUnavailable = """
        {"error":"no node is advertising embedding model 'no-such-embed-model'"}
        """;

    private const string RecordedInvalidMode = """
        {"error":"invalid mode 'semantic'; expected vector, keyword or hybrid"}
        """;

    // -----------------------------------------------------------------------------------------

    private static (InferHubCorpusClient Client, FakeHttpMessageHandler Handler) CreateClient(
        HttpStatusCode status,
        string body)
    {
        var handler = new FakeHttpMessageHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new InferHubCorpusClient(http), handler);
    }

    private static Stream FileBytes() => new MemoryStream(Encoding.UTF8.GetBytes("# notes\n\nsome text\n"));

    // ---- ingestion ---------------------------------------------------------------------------

    [Fact]
    public async Task IngestTextAsync_posts_json_to_the_documents_route()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedIngested);

        var result = await client.IngestTextAsync(
            "handbook",
            new TextDocument
            {
                Id = "onboarding",
                Text = "Error E-4021 means the coordinator refused a request.",
                Source = "handbook.md",
                ContentType = "text/markdown",
                Metadata = new Dictionary<string, string> { ["team"] = "platform" }
            });

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost:5080/api/collections/handbook/documents", request.RequestUri!.ToString());
        Assert.Contains("\"text\"", handler.RequestBodies[0]);
        Assert.Contains("\"team\":\"platform\"", handler.RequestBodies[0]);

        Assert.Equal("onboarding", result.DocumentId);
        Assert.Equal(IngestStatuses.Ingested, result.Status);
        Assert.Equal(1, result.ChunksEmbedded);
        Assert.False(result.IsPartial);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task IngestTextAsync_reports_unchanged_without_re_embedding()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedUnchanged);

        var result = await client.IngestTextAsync("handbook", new TextDocument { Id = "onboarding", Text = "…" });

        Assert.True(result.IsUnchanged);
        Assert.Equal(1, result.Chunks);
        Assert.Equal(0, result.ChunksEmbedded);
    }

    [Fact]
    public async Task IngestTextAsync_requires_text()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedIngested);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.IngestTextAsync("handbook", new TextDocument { Id = "x" }));
    }

    /// <summary>
    /// The hub answers a partial ingest with a <c>500</c> and a complete body. It is an outcome, not
    /// a transport failure, and the caller needs the id to resume.
    /// </summary>
    [Fact]
    public async Task IngestTextAsync_returns_a_partial_result_rather_than_throwing_on_500()
    {
        var (client, _) = CreateClient(HttpStatusCode.InternalServerError, RecordedPartial);

        var result = await client.IngestTextAsync("handbook", new TextDocument { Id = "z", Text = "hello there" });

        Assert.True(result.IsPartial);
        Assert.Equal("z", result.DocumentId);
        Assert.Equal(1, result.Chunks);
        Assert.Equal(0, result.ChunksEmbedded);
        Assert.Equal("no node is advertising embedding model 'no-such-embed-model'", result.Error);
    }

    [Fact]
    public async Task IngestTextAsync_still_throws_on_a_500_that_is_not_a_partial_result()
    {
        var (client, _) = CreateClient(HttpStatusCode.InternalServerError, """{"error":"the vector store is unavailable"}""");

        var error = await Assert.ThrowsAsync<InferHubException>(
            () => client.IngestTextAsync("handbook", new TextDocument { Text = "x" }));

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.Equal("the vector store is unavailable", error.Message);
    }

    /// <summary>
    /// Above <c>Tools:MaxStreamedBytes</c> the hub routes an upload from its leading form fields
    /// while the bytes are still arriving, so a field written after the file is a <c>400</c> naming
    /// it — and the buffered path below that ceiling accepts any order, which is what makes the
    /// mistake survive every test that only checks the fields are present.
    /// </summary>
    [Fact]
    public async Task IngestFileAsync_writes_every_field_before_the_file_part()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedIngested);

        await client.IngestFileAsync(
            "handbook",
            new FileDocument
            {
                Content = FileBytes(),
                FileName = "notes.md",
                ContentType = "text/markdown",
                Id = "architecture",
                Model = "nomic-embed-text",
                Metadata = new Dictionary<string, string> { ["team"] = "platform" }
            });

        var body = handler.RequestBodies[0];
        var id = body.IndexOf("name=id", StringComparison.Ordinal);
        var metadata = body.IndexOf("name=metadata", StringComparison.Ordinal);
        var model = body.IndexOf("name=model", StringComparison.Ordinal);
        var file = body.IndexOf("name=file", StringComparison.Ordinal);

        Assert.True(id >= 0 && metadata >= 0 && model >= 0 && file >= 0, body);
        Assert.True(id < file, "id must precede the file part");
        Assert.True(metadata < file, "metadata must precede the file part");
        Assert.True(model < file, "model must precede the file part");
    }

    /// <summary>
    /// The one multipart surface that sends the caller's file name: the hub resolves the extractor
    /// from the extension, stores it as each chunk's source, and falls back to it for the document
    /// id. An image upload deliberately drops it.
    /// </summary>
    [Fact]
    public async Task IngestFileAsync_sends_the_file_name_and_its_content_type()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedIngested);

        await client.IngestFileAsync(
            "handbook",
            new FileDocument { Content = FileBytes(), FileName = "notes.md", ContentType = "text/markdown" });

        var body = handler.RequestBodies[0];
        Assert.Contains("filename=notes.md", body);
        Assert.Contains("text/markdown", body);
    }

    [Fact]
    public async Task IngestFileAsync_requires_a_file_name()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedIngested);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.IngestFileAsync("handbook", new FileDocument { Content = FileBytes() }));
    }

    [Fact]
    public async Task IngestFileAsync_surfaces_the_node_owned_pdf_refusal()
    {
        var (client, _) = CreateClient(HttpStatusCode.UnsupportedMediaType, RecordedPdfOnNode);

        var error = await Assert.ThrowsAsync<InferHubException>(
            () => client.IngestFileAsync(
                "handbook",
                new FileDocument { Content = FileBytes(), FileName = "scan.pdf", ContentType = "application/pdf" }));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, error.StatusCode);
        Assert.Contains("ships with the coordinator only", error.Message);
    }

    [Fact]
    public async Task IngestFileAsync_surfaces_an_unsupported_format_verbatim()
    {
        var (client, _) = CreateClient(HttpStatusCode.UnsupportedMediaType, RecordedUnsupportedType);

        var error = await Assert.ThrowsAsync<InferHubException>(
            () => client.IngestFileAsync("handbook", new FileDocument { Content = FileBytes(), FileName = "thing.exe" }));

        Assert.Contains("supported: text/plain, text/markdown, text/html, application/json, application/pdf", error.Message);
    }

    // ---- the documents in a collection --------------------------------------------------------

    [Fact]
    public async Task ListDocumentsAsync_reads_the_envelope()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedDocumentList);

        var documents = await client.ListDocumentsAsync("handbook");

        Assert.Equal("http://localhost:5080/api/collections/handbook/documents", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(2, documents.Count);
        Assert.Equal("architecture", documents[0].Id);
        Assert.Equal("notes.md", documents[0].Source);
        Assert.Equal(DocumentStatuses.Complete, documents[0].Status);
        Assert.False(documents[0].IsPartial);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 20, 49, 45, TimeSpan.Zero).Date, documents[0].IngestedAt!.Value.Date);
    }

    [Fact]
    public async Task ListDocumentsAsync_throws_when_the_collection_does_not_exist()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedCollectionNotFound);

        var error = await Assert.ThrowsAsync<InferHubException>(() => client.ListDocumentsAsync("nope"));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("collection 'nope' does not exist", error.Message);
    }

    [Fact]
    public async Task GetDocumentAsync_reads_one_document()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedDocument);

        var document = await client.GetDocumentAsync("handbook", "onboarding");

        Assert.Equal(
            "http://localhost:5080/api/collections/handbook/documents/onboarding",
            handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("onboarding", document!.Id);
        Assert.Equal("text/markdown", document.MediaType);
        Assert.Equal(149, document.Bytes);
    }

    [Fact]
    public async Task GetDocumentAsync_answers_null_for_a_document_that_is_not_there()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedDocumentNotFound);

        Assert.Null(await client.GetDocumentAsync("handbook", "nope"));
    }

    /// <summary>
    /// <c>index</c> and <c>page</c> come back as strings here and a search hit's page is an
    /// <c>int</c> — the same chunk described in two types, because one route reads chunk metadata
    /// (a string map) and the other reads a parsed match.
    /// </summary>
    [Fact]
    public async Task GetChunksAsync_reads_the_string_typed_index_and_a_null_page()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedChunks);

        var chunks = await client.GetChunksAsync("handbook", "onboarding");

        Assert.Equal(
            "http://localhost:5080/api/collections/handbook/documents/onboarding/chunks",
            handler.Requests[0].RequestUri!.ToString());

        var chunk = Assert.Single(chunks);
        Assert.Equal("0", chunk.Index);
        Assert.Equal(0, chunk.IndexOrDefault);
        Assert.Null(chunk.Page);
        Assert.Null(chunk.PageOrDefault);
        Assert.StartsWith("Error E-4021", chunk.Text);
    }

    [Fact]
    public async Task GetChunksAsync_answers_empty_for_a_document_that_is_not_there()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedDocumentNotFound);

        Assert.Empty(await client.GetChunksAsync("handbook", "nope"));
    }

    [Fact]
    public async Task DeleteDocumentAsync_reports_how_many_chunks_went_with_it()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedDeleted);

        var deletion = await client.DeleteDocumentAsync("handbook", "policy.txt");

        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.True(deletion!.Deleted);
        Assert.Equal(1, deletion.Chunks);
        Assert.Equal("policy.txt", deletion.DocumentId);
    }

    [Fact]
    public async Task DeleteDocumentAsync_answers_null_when_there_was_nothing_to_delete()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedDocumentNotFound);

        Assert.Null(await client.DeleteDocumentAsync("handbook", "policy.txt"));
    }

    [Fact]
    public async Task Document_ids_are_escaped_into_the_path()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedDocument);

        await client.GetDocumentAsync("hand book", "notes/2026 q3.md");

        // AbsoluteUri, not ToString(): ToString() unescapes for display, which is exactly how an
        // assertion here can pass while the request on the wire is a different path.
        Assert.Equal(
            "http://localhost:5080/api/collections/hand%20book/documents/notes%2F2026%20q3.md",
            handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    // ---- search --------------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_posts_the_knobs_as_body_fields_and_no_headers()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedHybridSearch);

        await client.SearchAsync(
            "handbook",
            new SearchRequest("E-4021 approval") { Mode = RetrievalModes.Hybrid, K = 3, Rerank = true });

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost:5080/api/collections/handbook/search", request.RequestUri!.ToString());
        Assert.False(request.Headers.Contains("X-InferHub-Rerank"));
        Assert.False(request.Headers.Contains("X-InferHub-Retrieve-Mode"));

        var body = handler.RequestBodies[0];
        Assert.Contains("\"mode\":\"hybrid\"", body);
        Assert.Contains("\"k\":3", body);
        Assert.Contains("\"rerank\":true", body);
    }

    [Fact]
    public async Task SearchAsync_reads_the_hits()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedVectorSearch);

        var answer = await client.SearchAsync("handbook", "what does E-4021 mean");

        Assert.Equal(RetrievalModes.Vector, answer.Mode);
        var hit = Assert.Single(answer.Hits);
        Assert.Equal("onboarding", hit.DocumentId);
        Assert.Null(hit.Page);
        Assert.Equal(0.6114709941765204, hit.Score, 12);
    }

    /// <summary>
    /// The load-bearing one. A rerank reorders and leaves the scores alone, so the answer arrives
    /// with its best hit carrying a lower score than the one below it. The client hands the list
    /// over exactly as it came.
    /// </summary>
    [Fact]
    public async Task SearchAsync_keeps_a_reranked_answer_in_wire_order()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedRerankedSearch);

        var answer = await client.SearchAsync(
            "handbook",
            new SearchRequest("how do I get an expense approved") { Mode = RetrievalModes.Hybrid, Rerank = true });

        Assert.Equal("policy.txt", answer.Hits[0].DocumentId);
        Assert.Equal("onboarding", answer.Hits[1].DocumentId);

        // The first hit scores LOWER than the second. Sorting by score would undo the rerank.
        Assert.True(answer.Hits[0].Score < answer.Hits[1].Score);
    }

    [Fact]
    public async Task SearchAsync_surfaces_served_by_when_the_hub_sends_it()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedVectorSearch);
        handler.ResponseHeaders["X-InferHub-Served-By"] = "65075bfb-5968-48d6-8e54-9fc20814b73b";

        var answer = await client.SearchAsync("handbook", "x");

        Assert.Equal("65075bfb-5968-48d6-8e54-9fc20814b73b", answer.ServedBy);
    }

    [Fact]
    public async Task SearchAsync_leaves_served_by_null_when_the_header_is_absent()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedVectorSearch);

        Assert.Null((await client.SearchAsync("handbook", "x")).ServedBy);
    }

    /// <summary>
    /// A missing collection throws here and a missing document does not. Answering "no hits" for a
    /// collection name with a typo in it is how a retrieval system reports an empty corpus as a
    /// working one.
    /// </summary>
    [Fact]
    public async Task SearchAsync_throws_when_the_collection_does_not_exist()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedCollectionNotFound);

        var error = await Assert.ThrowsAsync<InferHubException>(() => client.SearchAsync("nope", "x"));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("collection 'nope' does not exist", error.Message);
    }

    [Fact]
    public async Task SearchAsync_maps_424_onto_the_retrieval_exception()
    {
        var (client, _) = CreateClient(HttpStatusCode.FailedDependency, RecordedRetrievalUnavailable);

        var error = await Assert.ThrowsAsync<InferHubRetrievalException>(() => client.SearchAsync("handbook", "x"));

        Assert.Equal("no node is advertising embedding model 'no-such-embed-model'", error.Message);
    }

    [Fact]
    public async Task SearchAsync_surfaces_an_invalid_mode_verbatim()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, RecordedInvalidMode);

        var error = await Assert.ThrowsAsync<InferHubException>(
            () => client.SearchAsync("handbook", new SearchRequest("x") { Mode = "semantic" }));

        Assert.Equal("invalid mode 'semantic'; expected vector, keyword or hybrid", error.Message);
    }

    [Fact]
    public async Task SearchAsync_requires_a_query()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedVectorSearch);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync("handbook", new SearchRequest()));
    }
}
