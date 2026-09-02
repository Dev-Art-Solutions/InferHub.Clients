using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Corpus;
using Microsoft.Extensions.DependencyInjection;

// Documents in, chunks out, and a search you can read.
//
// Point INFERHUB_BASE at a coordinator with VectorStore:Enabled=true, or at a standalone node with
// LocalApi:Retrieval:Enabled=true — the routes, the bodies and this file are the same either way.
// On a hub the collection must exist first (the admin plane creates it, with the dimension your
// embedding model produces); on a node, and for a client key carrying a collection scope, ingesting
// provisions it.
//
// Pass a file path to upload it; with no argument the sample ingests a few lines of text.

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");
var collection = Environment.GetEnvironmentVariable("INFERHUB_COLLECTION") ?? "handbook";
var embedModel = Environment.GetEnvironmentVariable("INFERHUB_EMBED_MODEL");
var rerankModel = Environment.GetEnvironmentVariable("INFERHUB_RERANK_MODEL");
var path = args.Length > 0 ? args[0] : null;

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = baseAddress;
    o.ApiKey = apiKey;
});

using var provider = services.BuildServiceProvider();
var corpus = provider.GetRequiredService<IInferHubCorpusClient>();

Console.WriteLine($"Target:     {baseAddress}");
Console.WriteLine($"Collection: {collection}");
Console.WriteLine();

try
{
    IngestResult ingest;

    if (path is not null)
    {
        // The stream is handed over and read as the request is written — a 200 MB PDF is never
        // buffered into a byte[] on the way.
        await using var file = File.OpenRead(path);
        ingest = await corpus.IngestFileAsync(collection, new FileDocument
        {
            Content = file,
            FileName = Path.GetFileName(path),
            Model = embedModel,
            Metadata = new Dictionary<string, string> { ["uploadedBy"] = "samples/Ingest" }
        });
    }
    else
    {
        ingest = await corpus.IngestTextAsync(collection, new TextDocument
        {
            Id = "onboarding",
            Source = "handbook.md",
            ContentType = "text/markdown",
            Model = embedModel,
            Text = """
                # Expenses

                Payroll runs on the fifth working day. Expenses over 500 EUR need approval from a
                line manager before they are submitted.

                # Errors

                Error E-4021 means the coordinator refused a request because no node holds the
                model. Wait for a node to join, or pull the model on an existing node.
                """
        });
    }

    Console.WriteLine($"{ingest.Status}: {ingest.DocumentId} — {ingest.ChunksEmbedded}/{ingest.Chunks} chunks embedded, {ingest.Bytes} bytes");

    // A partial ingest arrives as an HTTP 500 with a complete body, so it is returned rather than
    // thrown: the chunks that landed are real, and re-posting the same bytes resumes.
    if (ingest.IsPartial)
    {
        Console.WriteLine($"  ! partial — {ingest.Error}");
        Console.WriteLine("  re-post the same bytes once the cause is fixed; the id stays the same.");
    }

    Console.WriteLine();
    Console.WriteLine("documents:");
    foreach (var document in await corpus.ListDocumentsAsync(collection))
    {
        Console.WriteLine($"  {document.Id,-24} {document.Chunks,4} chunks  {document.Status}  {document.MediaType}");
    }

    var chunks = await corpus.GetChunksAsync(collection, ingest.DocumentId);
    Console.WriteLine();
    Console.WriteLine($"chunks of {ingest.DocumentId}:");
    foreach (var chunk in chunks)
    {
        var page = chunk.PageOrDefault is { } p ? $" p{p}" : string.Empty;
        Console.WriteLine($"  [{chunk.IndexOrDefault}{page}] {Preview(chunk.Text)}");
    }

    const string question = "how do I get an expense approved";
    Console.WriteLine();
    Console.WriteLine($"search ({(rerankModel is null ? "hybrid" : "hybrid, reranked")}): {question}");

    var answer = await corpus.SearchAsync(collection, new SearchRequest(question)
    {
        Mode = RetrievalModes.Hybrid,
        K = 3,
        EmbeddingModel = embedModel,
        Rerank = rerankModel is null ? null : true,
        Model = rerankModel
    });

    // Printed in the hub's order and never re-sorted: a rerank changes the order and leaves the
    // scores as retrieval computed them, so sorting by score would undo it.
    foreach (var hit in answer.Hits)
    {
        Console.WriteLine($"  {hit.Score,8:F4}  {hit.DocumentId,-24} {Preview(hit.Text)}");
    }

    if (answer.ServedBy is { } servedBy)
    {
        Console.WriteLine();
        Console.WriteLine($"served by: {servedBy}");
    }
}
catch (InferHubRetrievalException ex)
{
    Console.WriteLine($"[424 retrieval unavailable] {ex.Message}");
}
catch (InferHubException ex)
{
    Console.WriteLine($"[corpus error {(int)ex.StatusCode}] {ex.Message}");
}

static string Preview(string? text)
{
    var line = (text ?? string.Empty).ReplaceLineEndings(" ").Trim();
    return line.Length <= 80 ? line : line[..80] + "…";
}
