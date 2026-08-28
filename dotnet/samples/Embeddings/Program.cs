using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Ollama;
using Microsoft.Extensions.DependencyInjection;

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");
var model = Environment.GetEnvironmentVariable("INFERHUB_EMBED_MODEL") ?? "nomic-embed-text";

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = baseAddress;
    o.ApiKey = apiKey;
});

using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IInferHubClient>();

Console.WriteLine($"Coordinator: {baseAddress}");
Console.WriteLine($"Model:       {model}");
Console.WriteLine();

try
{
    var single = await client.EmbedAsync(EmbedRequest.FromText(model, "hello, world"));
    Console.WriteLine($"single: 1 vector, dim={single.Embeddings[0].Length}, first={single.Embeddings[0][0]:F4}");

    var batch = await client.EmbedAsync(EmbedRequest.FromTexts(model, new[]
    {
        "InferHub is a self-hosted inference mesh.",
        "The client talks to a coordinator over HTTP.",
        "Embeddings are batch by default."
    }));
    Console.WriteLine($"batch:  {batch.Embeddings.Length} vectors, dim={batch.Embeddings[0].Length}");

    var legacy = await client.EmbedLegacyAsync(new EmbeddingsRequest
    {
        Model = model,
        Prompt = "legacy shape still works"
    });
    Console.WriteLine($"legacy: 1 vector, dim={legacy.Embedding.Length}");
}
catch (InferHubException ex)
{
    Console.WriteLine($"[embed error {(int)ex.StatusCode}] {ex.Message}");
}
