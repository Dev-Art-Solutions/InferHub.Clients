using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Vector;
using Microsoft.Extensions.DependencyInjection;

// A tiny end-to-end: embed a handful of docs into a collection, then query it by text.
// Needs a coordinator with VectorStore:Enabled=true and a collection already created
// (see the admin plane, or the coordinator console). Set INFERHUB_COLLECTION to match.

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");
var model = Environment.GetEnvironmentVariable("INFERHUB_EMBED_MODEL") ?? "nomic-embed-text";
var collection = Environment.GetEnvironmentVariable("INFERHUB_COLLECTION") ?? "mini-rag";

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = baseAddress;
    o.ApiKey = apiKey;
});

using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IInferHubClient>();

Console.WriteLine($"Coordinator: {baseAddress}");
Console.WriteLine($"Collection:  {collection}");
Console.WriteLine($"Model:       {model}");
Console.WriteLine();

var docs = new (string Id, string Text)[]
{
    ("doc-1", "InferHub is a self-hosted, Ollama-compatible inference mesh."),
    ("doc-2", "A coordinator runs on an always-on host; GPU nodes reach out over SignalR."),
    ("doc-3", "The vector store persists records and rebuilds its index on startup."),
    ("doc-4", "Clients re-send full conversation history each turn; the hub keeps no content."),
};

try
{
    foreach (var (id, text) in docs)
    {
        var record = await client.UpsertAsync(
            collection,
            VectorUpsert.FromText(id, text, model)
                .WithPayload(text)
                .WithMetadata(new Dictionary<string, string> { ["kind"] = "doc" }));
        Console.WriteLine($"upserted {record.Id} (dim={record.Vector.Length}, seqNo={record.SeqNo})");
    }

    Console.WriteLine();
    const string question = "How do GPU machines connect to the coordinator?";
    Console.WriteLine($"query: {question}");
    Console.WriteLine();

    var matches = await client.QueryAsync(collection, VectorQuery.FromText(question, model, k: 3));
    foreach (var match in matches)
    {
        Console.WriteLine($"  {match.Score,6:F3}  {match.Id}");
    }

    var top = matches.Count > 0 ? await client.GetRecordAsync(collection, matches[0].Id) : null;
    if (top is not null)
    {
        Console.WriteLine();
        Console.WriteLine($"top hit text: {top.Payload.As<string>() ?? "(payload not stored)"}");
    }
}
catch (InferHubException ex)
{
    Console.WriteLine($"[vector error {(int)ex.StatusCode}] {ex.Message}");
}
