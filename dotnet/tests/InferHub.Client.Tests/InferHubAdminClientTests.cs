using System.Net;
using System.Text.Json;
using InferHub.Client.Configuration;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Http;
using InferHub.Client.Models.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace InferHub.Client.Tests;

public class InferHubAdminClientTests
{
    private const string NodeJson = """
        {
          "connectionId": "conn-1",
          "nodeId": "n1",
          "name": "alpha",
          "ollamaEndpoint": "http://127.0.0.1:11434",
          "version": "2.0.0",
          "lastSeenUtc": "2026-07-08T00:00:00Z",
          "ageSeconds": 1.5,
          "inFlight": 2,
          "localInFlight": 2,
          "modelCount": 3,
          "labels": { "gpu": "rtx4090" },
          "maxConcurrency": 4,
          "cordoned": true,
          "lastAction": { "action": "cordon", "atUtc": "2026-07-08T00:00:00Z", "by": "local" }
        }
        """;

    private static (InferHubAdminClient Client, FakeHttpMessageHandler Handler) CreateClient(HttpStatusCode status, string body)
    {
        var handler = new FakeHttpMessageHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new InferHubAdminClient(http), handler);
    }

    private static InferHubAdminClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return new InferHubAdminClient(http);
    }

    [Fact]
    public async Task ListNodesAsync_parses_admin_nodes()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, $"[{NodeJson}]");

        var nodes = await client.ListNodesAsync();

        Assert.Single(nodes);
        var node = nodes[0];
        Assert.Equal("n1", node.NodeId);
        Assert.Equal("alpha", node.Name);
        Assert.Equal(2, node.InFlight);
        Assert.Equal(4, node.MaxConcurrency);
        Assert.True(node.Cordoned);
        Assert.Equal("rtx4090", node.Labels!["gpu"]);
        Assert.Equal("cordon", node.LastAction!.Action);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.EndsWith("api/admin/nodes", handler.Requests[0].RequestUri!.ToString());
    }

    [Theory]
    [InlineData("cordon")]
    [InlineData("uncordon")]
    [InlineData("deregister")]
    public async Task Node_actions_post_to_the_right_endpoint(string action)
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"nodeId":"n1"}""");

        var task = action switch
        {
            "cordon" => client.CordonAsync("n1"),
            "uncordon" => client.UncordonAsync("n1"),
            _ => client.DeregisterAsync("n1"),
        };
        await task;

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith($"api/admin/nodes/n1/{action}", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task CordonAsync_unknown_node_surfaces_404()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"node 'nope' not found"}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() => client.CordonAsync("nope"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("node 'nope' not found", ex.Message);
    }

    [Fact]
    public async Task CordonAsync_rejects_blank_node_id()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, "{}");
        await Assert.ThrowsAsync<ArgumentException>(() => client.CordonAsync(" "));
    }

    [Fact]
    public async Task ListCollectionsAsync_parses_collections_and_placement()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """
            {
              "collections": [ { "name": "docs", "dimension": 768, "distance": "cosine", "recordCount": 42, "operations": 50 } ],
              "placement": [ { "collection": "docs", "targetReplicas": 2, "liveReplicas": 1, "replicaNodes": ["n1"] } ]
            }
            """);

        var result = await client.ListCollectionsAsync();

        Assert.Single(result.Collections);
        Assert.Equal("docs", result.Collections[0].Name);
        Assert.Equal(768, result.Collections[0].Dimension);
        Assert.Equal(42, result.Collections[0].RecordCount);
        Assert.Single(result.Placement);
        Assert.Equal(1, result.Placement[0].LiveReplicas);
        Assert.Equal(new[] { "n1" }, result.Placement[0].ReplicaNodes);
        Assert.EndsWith("api/admin/vector/collections", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetCollectionAsync_parses_detail_with_underReplicated_and_stats()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """
            {
              "collection": { "name": "docs", "dimension": 768, "distance": "cosine", "recordCount": 42, "operations": 50 },
              "placement": { "collection": "docs", "targetReplicas": 2, "liveReplicas": 1, "replicaNodes": ["n1"] },
              "underReplicated": true,
              "stats": { "collection": "docs", "queries": 10, "queryLatencyAvgMs": 1.25 }
            }
            """);

        var detail = await client.GetCollectionAsync("docs");

        Assert.NotNull(detail);
        Assert.True(detail!.UnderReplicated);
        Assert.Equal("docs", detail.Collection.Name);
        Assert.Equal(10, detail.Stats!.Queries);
        Assert.Equal(1.25, detail.Stats.QueryLatencyAvgMs);
        Assert.EndsWith("api/admin/vector/collections/docs", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetCollectionAsync_returns_null_on_404()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"collection 'nope' not found"}""");
        Assert.Null(await client.GetCollectionAsync("nope"));
    }

    [Fact]
    public async Task CreateCollectionAsync_sends_body_and_parses_created_collection()
    {
        var (client, handler) = CreateClient(HttpStatusCode.Created,
            """{ "name": "docs", "dimension": 768, "distance": "cosine", "recordCount": 0, "operations": 0 }""");

        var info = await client.CreateCollectionAsync("docs", 768, "cosine");

        Assert.Equal("docs", info.Name);
        Assert.Equal(768, info.Dimension);

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("docs", sent.GetProperty("name").GetString());
        Assert.Equal(768, sent.GetProperty("dimension").GetInt32());
        Assert.Equal("cosine", sent.GetProperty("distance").GetString());
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("api/admin/vector/collections", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateCollectionAsync_omits_null_distance()
    {
        var (client, handler) = CreateClient(HttpStatusCode.Created,
            """{ "name": "docs", "dimension": 8, "distance": "cosine", "recordCount": 0, "operations": 0 }""");

        await client.CreateCollectionAsync("docs", 8);

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.False(sent.TryGetProperty("distance", out _));
    }

    [Fact]
    public async Task CreateCollectionAsync_duplicate_surfaces_409()
    {
        var (client, _) = CreateClient(HttpStatusCode.Conflict, """{"error":"collection 'docs' already exists"}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() => client.CreateCollectionAsync("docs", 768));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task CreateCollectionAsync_rejects_bad_arguments()
    {
        var (client, _) = CreateClient(HttpStatusCode.Created, "{}");

        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateCollectionAsync(" ", 768));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.CreateCollectionAsync("docs", 0));
    }

    [Fact]
    public async Task DropCollectionAsync_deletes_and_unknown_collection_surfaces_404()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"collection":"docs","dropped":true}""");
        await client.DropCollectionAsync("docs");
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.EndsWith("api/admin/vector/collections/docs", handler.Requests[0].RequestUri!.ToString());

        var (missing, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"collection 'nope' not found"}""");
        var ex = await Assert.ThrowsAsync<InferHubException>(() => missing.DropCollectionAsync("nope"));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task RebuildAsync_posts_to_rebuild_endpoint()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"collection":"docs","rebuilt":true}""");

        await client.RebuildAsync("docs");

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("api/admin/vector/collections/docs/rebuild", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Admin_bearer_handler_sends_the_admin_key()
    {
        var fake = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var handler = new AdminBearerAuthorizationHandler(new InferHubClientOptions { ApiKey = "client-key", AdminApiKey = "admin-key" })
        {
            InnerHandler = fake
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubAdminClient(http);

        await client.ListNodesAsync();

        Assert.Equal("Bearer admin-key", fake.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public void AddInferHubClient_registers_the_admin_client()
    {
        var services = new ServiceCollection();
        services.AddInferHubClient(o =>
        {
            o.BaseAddress = new Uri("http://localhost:5080/");
            o.AdminApiKey = "admin-key";
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IInferHubAdminClient>());
        Assert.NotNull(provider.GetService<IInferHubClient>());
    }

    [Fact]
    public async Task StreamAdminEventsAsync_yields_snapshot_and_vector_events()
    {
        var handler = new StreamingHttpMessageHandler();
        var client = CreateClient(handler);

        handler.EnqueueLine("event: snapshot");
        handler.EnqueueLine($$"""data: { "nodes": [{{NodeJson.ReplaceLineEndings(" ")}}] }""");
        handler.EnqueueLine("");
        handler.EnqueueLine(": keepalive comment");
        handler.EnqueueLine("event: vector.replica.assigned");
        handler.EnqueueLine("""data: { "sequence": 7, "kind": "vector.replica.assigned", "collection": "docs", "atUtc": "2026-07-08T00:00:00Z", "data": { "node": "n1" } }""");
        handler.EnqueueLine("");
        handler.Complete();

        var events = new List<AdminEvent>();
        await foreach (var ev in client.StreamAdminEventsAsync())
        {
            events.Add(ev);
        }

        Assert.Equal(2, events.Count);

        Assert.True(events[0].IsSnapshot);
        Assert.Equal("snapshot", events[0].Event);
        Assert.Equal("n1", events[0].Nodes![0].NodeId);

        Assert.False(events[1].IsSnapshot);
        Assert.Equal("vector.replica.assigned", events[1].Event);
        Assert.Equal(7, events[1].Sequence);
        Assert.Equal("docs", events[1].Collection);
        Assert.Equal("n1", events[1].Data!.Value.GetProperty("node").GetString());

        Assert.EndsWith("api/admin/stream", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task StreamAdminEventsAsync_with_reconnect_resumes_after_server_close()
    {
        const string firstStream = "event: snapshot\ndata: {\"nodes\":[]}\n\n";
        const string secondStream = "event: vector.collection.created\ndata: {\"sequence\":1,\"kind\":\"vector.collection.created\",\"collection\":\"docs\",\"atUtc\":\"2026-07-08T00:00:00Z\"}\n\n";

        var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, firstStream, "text/event-stream");
        handler.Enqueue(HttpStatusCode.OK, secondStream, "text/event-stream");

        var client = CreateClient(handler);
        var options = new AdminStreamOptions { InitialBackoff = TimeSpan.FromMilliseconds(1), MaxBackoff = TimeSpan.FromMilliseconds(4) };

        var events = new List<AdminEvent>();
        await foreach (var ev in client.StreamAdminEventsAsync(options))
        {
            events.Add(ev);
            if (events.Count == 2)
            {
                break;
            }
        }

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(events[0].IsSnapshot);
        Assert.Equal("vector.collection.created", events[1].Event);
    }

    [Fact]
    public async Task StreamAdminEventsAsync_with_reconnect_never_retries_auth_failures()
    {
        var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, """{"error":"missing admin bearer token"}""");

        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InferHubException>(async () =>
        {
            await foreach (var _ in client.StreamAdminEventsAsync(new AdminStreamOptions()))
            {
            }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task StreamAdminEventsAsync_without_reconnect_ends_when_the_server_closes()
    {
        var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "event: snapshot\ndata: {\"nodes\":[]}\n\n", "text/event-stream");

        var client = CreateClient(handler);
        var options = new AdminStreamOptions { Reconnect = false };

        var count = 0;
        await foreach (var _ in client.StreamAdminEventsAsync(options))
        {
            count++;
        }

        Assert.Equal(1, count);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DrainAsync_cordons_then_polls_until_in_flight_reaches_zero()
    {
        var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"nodeId":"n1","cordoned":true}""");
        handler.Enqueue(HttpStatusCode.OK, """[{"nodeId":"n1","inFlight":2,"cordoned":true}]""");
        handler.Enqueue(HttpStatusCode.OK, """[{"nodeId":"n1","inFlight":0,"cordoned":true}]""");

        var client = CreateClient(handler);

        var node = await client.DrainAsync("n1", pollInterval: TimeSpan.FromMilliseconds(1));

        Assert.NotNull(node);
        Assert.Equal(0, node!.InFlight);
        Assert.True(node.Cordoned);
        Assert.Equal(3, handler.Requests.Count);
        Assert.EndsWith("api/admin/nodes/n1/cordon", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
    }

    [Fact]
    public async Task DrainAsync_returns_null_when_the_node_leaves_the_fleet()
    {
        var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"nodeId":"n1","cordoned":true}""");
        handler.Enqueue(HttpStatusCode.OK, "[]");

        var client = CreateClient(handler);

        Assert.Null(await client.DrainAsync("n1", pollInterval: TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public async Task Per_call_timeout_surfaces_as_TimeoutException()
    {
        var handler = new NeverRespondingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubAdminClient(http, new InferHubClientOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        await Assert.ThrowsAsync<TimeoutException>(() => client.ListNodesAsync());
    }

    [Fact]
    public async Task Per_call_timeout_does_not_swallow_caller_cancellation()
    {
        var handler = new NeverRespondingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubAdminClient(http, new InferHubClientOptions { Timeout = TimeSpan.FromSeconds(30) });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListNodesAsync(cts.Token));
    }

    private sealed class NeverRespondingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
