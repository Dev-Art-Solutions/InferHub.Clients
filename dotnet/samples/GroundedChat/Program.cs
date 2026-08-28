using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Ollama;
using InferHub.Client.Models.Vector;
using Microsoft.Extensions.DependencyInjection;

// Grounded ("RAG") chat: seed a collection with a couple of docs, then ask a question with
// retrieval turned on. The coordinator pulls the top matches from the collection, grounds the
// prompt, and echoes the record ids it used in the X-InferHub-Sources header — surfaced here as
// ChatResponse.SourceIds. Orchestration happens on the hub; compute happens on the fleet.
//
// Needs a coordinator with VectorStore:Enabled=true and the collection already created.
// Set INFERHUB_COLLECTION to match.

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");
var chatModel = Environment.GetEnvironmentVariable("INFERHUB_CHAT_MODEL") ?? "llama3";
var embedModel = Environment.GetEnvironmentVariable("INFERHUB_EMBED_MODEL") ?? "nomic-embed-text";
var collection = Environment.GetEnvironmentVariable("INFERHUB_COLLECTION") ?? "grounded-chat";

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
Console.WriteLine();

var docs = new (string Id, string Text)[]
{
    ("kb-1", "InferHub nodes reach out to the coordinator over SignalR; the coordinator never dials in."),
    ("kb-2", "The vector store persists records to disk and rebuilds its index on startup."),
    ("kb-3", "Clients re-send full conversation history each turn; the hub retains no message content."),
};

try
{
    foreach (var (id, text) in docs)
    {
        await client.UpsertAsync(collection, VectorUpsert.FromText(id, text, embedModel).WithPayload(text));
    }

    const string question = "How do GPU machines connect to the coordinator?";
    Console.WriteLine($"Q: {question}");
    Console.WriteLine();

    // The one new thing this phase: pass retrieval options. K docs are pulled from the
    // collection and prepended to the prompt before it reaches a chat node.
    var response = await client.ChatAsync(
        new ChatRequest
        {
            Model = chatModel,
            Messages = new[] { new ChatMessage { Role = "user", Content = question } },
        },
        InferHubCallOptions.ForRetrieval(collection, k: 3, model: embedModel));

    Console.WriteLine($"A: {response.Message?.Content}");
    Console.WriteLine();
    Console.WriteLine(response.SourceIds is { Count: > 0 } sources
        ? $"grounded on: {string.Join(", ", sources)}"
        : "grounded on: (no sources returned)");
}
catch (InferHubRetrievalException ex)
{
    // 424 — retrieval could not be satisfied (store unavailable, or OnMissing=error with no hits).
    Console.WriteLine($"[retrieval unavailable {(int)ex.StatusCode}] {ex.Message}");
}
catch (InferHubException ex)
{
    Console.WriteLine($"[error {(int)ex.StatusCode}] {ex.Message}");
}
