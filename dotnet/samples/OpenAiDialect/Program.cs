using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.OpenAi;
using Microsoft.Extensions.DependencyInjection;

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");
var model = Environment.GetEnvironmentVariable("INFERHUB_MODEL") ?? "llama3";

// INFERHUB_PROVIDER names a cloud provider to steer to; leave it unset and the prompt stays on
// the fleet, which is what ForFleetOnly() asks for explicitly.
var provider = Environment.GetEnvironmentVariable("INFERHUB_PROVIDER");

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = baseAddress;
    o.ApiKey = apiKey;
});

using var serviceProvider = services.BuildServiceProvider();
var openAi = serviceProvider.GetRequiredService<IInferHubOpenAiClient>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Coordinator: {baseAddress}");
Console.WriteLine($"Model:       {model}");
Console.WriteLine(provider is null
    ? "Steer:       node (no cloud provider sees this prompt)"
    : $"Steer:       {provider}");
Console.WriteLine();

var models = await openAi.ListModelsAsync(cts.Token);
Console.WriteLine($"/v1/models advertises {models.Count} model(s).");
foreach (var entry in models.Take(3))
{
    var capabilities = entry.Capabilities is { Count: > 0 } kinds ? string.Join(", ", kinds) : "unknown";
    Console.WriteLine($"  {entry.Id}  [{capabilities}]");
}

Console.WriteLine();

var options = provider is null
    ? InferHubCallOptions.ForFleetOnly()
    : InferHubCallOptions.ForProvider(provider);

var request = new ChatCompletionRequest
{
    Model = model,
    Messages = new[]
    {
        ChatCompletionMessage.System("You are a terse assistant."),
        ChatCompletionMessage.User("In two sentences: what is an inference mesh?")
    },
    StreamOptions = new ChatCompletionStreamOptions { IncludeUsage = true }
};

try
{
    string? servedBy = null;
    OpenAiUsage? usage = null;

    await foreach (var chunk in openAi.StreamChatCompletionAsync(request, options, cts.Token))
    {
        servedBy ??= chunk.ServedBy;

        // The usage frame arrives with no choices at all — it is the only place a streamed call
        // reports token counts, so it is read rather than skipped.
        if (chunk.Usage is { } counts)
        {
            usage = counts;
            continue;
        }

        Console.Write(chunk.Choices.FirstOrDefault()?.Delta?.Content);
    }

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine($"Served by:   {servedBy ?? "(no X-InferHub-Served-By header)"}");
    Console.WriteLine(usage is null
        ? "Usage:       not reported"
        : $"Usage:       {usage.PromptTokens} prompt + {usage.CompletionTokens} completion = {usage.TotalTokens}");
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine("[cancelled]");
}
catch (InferHubOpenAiException ex)
{
    // A refused steer lands here: 400, invalid_request_error, and one sentence naming the pair you
    // asked for. The hub answers the same sentence for an unknown provider, a disabled one and a
    // real one that maps a different model.
    Console.WriteLine();
    Console.WriteLine($"[{(int)ex.StatusCode} {ex.ErrorType}] {ex.Message}");
}
catch (InferHubException ex)
{
    Console.WriteLine();
    Console.WriteLine($"[error {(int)ex.StatusCode}] {ex.Message}");
}
