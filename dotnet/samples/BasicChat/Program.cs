using InferHub.Client;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Ollama;
using Microsoft.Extensions.DependencyInjection;

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");
var model = Environment.GetEnvironmentVariable("INFERHUB_MODEL") ?? "llama3";

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

var healthy = await client.PingAsync();
Console.WriteLine($"/health -> {(healthy ? "ok" : "unavailable")}");
if (!healthy)
{
    return;
}

var tags = await client.ListModelsAsync();
Console.WriteLine($"Models on the mesh: {string.Join(", ", tags.Models.Select(m => m.Name))}");
Console.WriteLine();

var chat = await client.ChatAsync(new ChatRequest
{
    Model = model,
    Messages = new[]
    {
        new ChatMessage { Role = "system", Content = "You are a terse assistant." },
        new ChatMessage { Role = "user", Content = "Say hi in one sentence." }
    }
});

Console.WriteLine("Assistant:");
Console.WriteLine(chat.Message?.Content);
