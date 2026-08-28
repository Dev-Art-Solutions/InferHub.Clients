using InferHub.Client.Configuration;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Ollama;
using Microsoft.Extensions.DependencyInjection;

namespace InferHub.Client.Tests;

/// <summary>
/// Marks a test that needs a live coordinator. It self-skips unless
/// <c>INFERHUB_TEST_BASEADDRESS</c> is set, so CI and local runs stay green by default while
/// the round-trip still runs on demand against a real coordinator.
/// </summary>
public sealed class RequiresCoordinatorFactAttribute : FactAttribute
{
    public const string BaseAddressVar = "INFERHUB_TEST_BASEADDRESS";
    public const string ApiKeyVar = "INFERHUB_TEST_APIKEY";
    public const string ModelVar = "INFERHUB_TEST_MODEL";

    public RequiresCoordinatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BaseAddressVar)))
        {
            Skip = $"Set {BaseAddressVar} (and optionally {ApiKeyVar}, {ModelVar}) to run integration tests.";
        }
    }
}

/// <summary>
/// End-to-end round-trip against a running coordinator. Opt-in via environment variables:
/// <list type="bullet">
///   <item><c>INFERHUB_TEST_BASEADDRESS</c> — e.g. <c>http://localhost:5080</c> (required).</item>
///   <item><c>INFERHUB_TEST_APIKEY</c> — client key for non-loopback coordinators (optional).</item>
///   <item><c>INFERHUB_TEST_MODEL</c> — a model tag to exercise a real chat (optional).</item>
/// </list>
/// </summary>
public class IntegrationTests
{
    private static IInferHubClient CreateClient()
    {
        var services = new ServiceCollection();
        services.AddInferHubClient(o =>
        {
            o.BaseAddress = new Uri(Environment.GetEnvironmentVariable(RequiresCoordinatorFactAttribute.BaseAddressVar)!);
            o.ApiKey = Environment.GetEnvironmentVariable(RequiresCoordinatorFactAttribute.ApiKeyVar);
            o.MaxRetryAttempts = 3;
        });
        return services.BuildServiceProvider().GetRequiredService<IInferHubClient>();
    }

    [RequiresCoordinatorFact]
    public async Task Health_and_status_round_trip()
    {
        var client = CreateClient();

        Assert.True(await client.PingAsync(), "coordinator /health should return 2xx");

        var status = await client.GetStatusAsync();
        Assert.NotNull(status);
    }

    [RequiresCoordinatorFact]
    public async Task Model_list_round_trips()
    {
        var client = CreateClient();

        var models = await client.ListModelsAsync();

        Assert.NotNull(models);
        Assert.NotNull(models.Models);
    }

    [RequiresCoordinatorFact]
    public async Task Blocking_chat_round_trips_when_a_model_is_configured()
    {
        var model = Environment.GetEnvironmentVariable(RequiresCoordinatorFactAttribute.ModelVar);
        if (string.IsNullOrWhiteSpace(model))
        {
            return; // no model to exercise — the health/status/models tests already cover connectivity
        }

        var client = CreateClient();

        var response = await client.ChatAsync(new ChatRequest
        {
            Model = model,
            Messages = new[] { new ChatMessage { Role = "user", Content = "Reply with the single word: pong." } }
        });

        Assert.True(response.Done);
        Assert.False(string.IsNullOrWhiteSpace(response.Message?.Content));
    }
}
