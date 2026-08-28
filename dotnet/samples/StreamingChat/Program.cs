using InferHub.Client;
using InferHub.Client.Exceptions;
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

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Coordinator: {baseAddress}");
Console.WriteLine($"Model:       {model}");
Console.WriteLine("Press Ctrl+C to cancel mid-stream.");
Console.WriteLine();

var request = new ChatRequest
{
    Model = model,
    Messages = new[]
    {
        new ChatMessage { Role = "system", Content = "You are a terse assistant." },
        new ChatMessage { Role = "user", Content = "Give a two-sentence tour of streaming NDJSON." }
    }
};

try
{
    await foreach (var chunk in client.ChatStreamAsync(request, cts.Token))
    {
        Console.Write(chunk.Message?.Content);
    }
    Console.WriteLine();
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine("[cancelled]");
}
catch (InferHubException ex)
{
    Console.WriteLine();
    Console.WriteLine($"[stream error {(int)ex.StatusCode}] {ex.Message}");
}
