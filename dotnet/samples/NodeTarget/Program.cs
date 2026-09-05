using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Node;
using InferHub.Client.Models.Ollama;
using Microsoft.Extensions.DependencyInjection;

// Phase 14: a node is a base address, not a second client. Point the same InferHub.Client at a
// coordinator or a solo node — INFERHUB_BASE decides which — and ask it what it is instead of
// guessing. Client key only; no admin key involved, because a node has no admin plane.

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = baseAddress;
    o.ApiKey = apiKey;
});

using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IInferHubClient>();

Console.WriteLine($"Target: {baseAddress}");

var probe = await client.ProbeAsync();
Console.WriteLine($"Kind: {probe.Kind}   Version: {probe.Version}");

if (probe.Kind == InferHubTargetKind.Hub)
{
    Console.WriteLine($"Hub — {probe.HubStatus!.Nodes?.Count ?? 0} node(s) connected.");
    Console.WriteLine("Admin plane (fleet ops, collection lifecycle) lives on IInferHubAdminClient — see samples/FleetOps.");
}
else
{
    var node = probe.NodeStatus!;
    Console.WriteLine($"Solo node '{node.Name}' — backend={node.Backend?.Name} ({node.Backend?.Health ?? "unsupervised"})");
    Console.WriteLine($"GPU: cuda={node.Gpu?.Cuda} devices={node.Gpu?.Devices} [{string.Join(", ", node.Gpu?.Names ?? [])}]");
    Console.WriteLine($"Capabilities: {string.Join(", ", node.Capabilities ?? [])}");

    // Node-only collection lifecycle — no admin key, no admin plane on this address at all.
    var collections = await client.ListNodeCollectionsAsync();
    Console.WriteLine($"Collections ({collections.Count}):");
    foreach (var c in collections)
    {
        Console.WriteLine($"  {c.Name,-16} dim={c.Dimension} {c.Distance} records={c.RecordCount}");
    }

    // A capability this node's own backend cannot serve at all is a permanent 501, not a hang.
    try
    {
        await client.EmbedAsync(EmbedRequest.FromText("nomic-embed-text", "probe"));
    }
    catch (InferHubException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotImplemented)
    {
        Console.WriteLine($"[embed refused, permanently] {ex.Message}");
    }
    catch (InferHubException ex) when (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
    {
        Console.WriteLine($"[embed refused, retry after {ex.RetryAfter}] {ex.Message}");
    }
}
