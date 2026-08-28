using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using Microsoft.Extensions.DependencyInjection;

// Fleet + vector ops from C#: list nodes, cordon → drain → uncordon the busiest node,
// walk the vector collections, then tail the live admin SSE stream for a few seconds.
// Needs an admin key on remote coordinators (INFERHUB_ADMIN_API_KEY); loopback works
// without one unless the coordinator sets Auth:RequireAuthForLoopback=true.

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var adminApiKey = Environment.GetEnvironmentVariable("INFERHUB_ADMIN_API_KEY");

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = baseAddress;
    o.AdminApiKey = adminApiKey;
});

using var provider = services.BuildServiceProvider();
var admin = provider.GetRequiredService<IInferHubAdminClient>();

Console.WriteLine($"Coordinator: {baseAddress}");
Console.WriteLine();

try
{
    // --- Fleet -------------------------------------------------------------
    var nodes = await admin.ListNodesAsync();
    Console.WriteLine($"Nodes ({nodes.Count}):");
    foreach (var n in nodes)
    {
        var state = n.Cordoned ? "cordoned" : "routable";
        Console.WriteLine($"  {n.NodeId,-16} {n.Name,-12} in-flight={n.InFlight} models={n.ModelCount} [{state}]");
    }

    if (nodes.Count > 0)
    {
        var target = nodes.OrderByDescending(n => n.InFlight).First();
        Console.WriteLine();
        Console.WriteLine($"cordon + drain {target.NodeId} …");
        var drained = await admin.DrainAsync(target.NodeId, pollInterval: TimeSpan.FromSeconds(1));
        Console.WriteLine(drained is null
            ? "node left the fleet while draining"
            : $"drained (in-flight={drained.InFlight})");

        await admin.UncordonAsync(target.NodeId);
        Console.WriteLine($"uncordoned {target.NodeId}");
    }

    // --- Vector collections --------------------------------------------------
    Console.WriteLine();
    var collections = await admin.ListCollectionsAsync();
    Console.WriteLine($"Collections ({collections.Collections.Count}):");
    foreach (var c in collections.Collections)
    {
        var placement = collections.Placement.FirstOrDefault(p => p.Collection == c.Name);
        var detail = await admin.GetCollectionAsync(c.Name);
        var health = detail?.UnderReplicated == true ? "UNDER-REPLICATED" : "ok";
        Console.WriteLine($"  {c.Name,-16} dim={c.Dimension} {c.Distance} records={c.RecordCount} " +
                          $"replicas={placement?.LiveReplicas}/{placement?.TargetReplicas} [{health}]");
    }

    // --- Live admin stream ---------------------------------------------------
    Console.WriteLine();
    Console.WriteLine("Tailing /api/admin/stream for 15s (snapshots + vector.* events) …");
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try
    {
        await foreach (var ev in admin.StreamAdminEventsAsync(cancellationToken: cts.Token))
        {
            if (ev.IsSnapshot)
            {
                var busy = ev.Nodes!.Sum(n => n.InFlight);
                Console.WriteLine($"  snapshot: {ev.Nodes!.Count} node(s), {busy} in flight");
            }
            else
            {
                Console.WriteLine($"  #{ev.Sequence} {ev.Event} collection={ev.Collection}");
            }
        }
    }
    catch (OperationCanceledException)
    {
        // 15s window elapsed
    }
}
catch (InferHubException ex)
{
    Console.WriteLine($"[admin error {(int)ex.StatusCode}] {ex.Message}");
}
