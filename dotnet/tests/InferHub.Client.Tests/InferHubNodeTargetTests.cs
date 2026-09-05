using System.Net;
using InferHub.Client.Exceptions;
using InferHub.Client.Models.Node;

namespace InferHub.Client.Tests;

/// <summary>
/// Phase 14 — a node is a base address, not a second client. <see cref="InferHubClient.ProbeAsync"/>
/// and the node-only methods, plus the corrected <c>501</c> vs <c>503</c> vendor-capability split.
/// </summary>
public class InferHubNodeTargetTests
{
    private static (InferHubClient Client, FakeHttpMessageHandler Handler) CreateClient(HttpStatusCode status, string body)
    {
        var handler = new FakeHttpMessageHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new InferHubClient(http), handler);
    }

    // ---- ProbeAsync --------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_reads_a_hub_document_as_Kind_Hub()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """
            {"coordinatorVersion":"3.37.0","nowUtc":"2026-09-05T00:00:00Z","uptimeSeconds":12.5,
             "nodes":[{"nodeId":"n1","name":"gpu-1"}],"models":[{"name":"llama3"}]}
            """);

        var probe = await client.ProbeAsync();

        Assert.Equal(InferHubTargetKind.Hub, probe.Kind);
        Assert.Equal("3.37.0", probe.Version);
        Assert.NotNull(probe.HubStatus);
        Assert.Null(probe.NodeStatus);
        Assert.Single(handler.Requests);
        Assert.Equal("api/status", handler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'));
    }

    [Fact]
    public async Task ProbeAsync_reads_a_solo_node_document_as_Kind_SoloNode()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, """
            {"mode":"solo","nodeVersion":"3.37.0","nowUtc":"2026-09-05T00:00:00Z","name":"gpu-box-1",
             "backend":{"name":"ollama","endpoint":"http://localhost:11434","health":"healthy"},
             "concurrency":{"limit":4,"inFlight":1},
             "gpu":{"cuda":true,"devices":1,"names":["RTX 4090"]},
             "capabilities":["chat","generate","embed"],
             "retrieval":{"enabled":false},
             "models":[{"name":"llama3","digest":"abc","size":4661211648}]}
            """);

        var probe = await client.ProbeAsync();

        Assert.Equal(InferHubTargetKind.SoloNode, probe.Kind);
        Assert.Equal("3.37.0", probe.Version);
        Assert.Null(probe.HubStatus);
        Assert.NotNull(probe.NodeStatus);
        Assert.Equal("gpu-box-1", probe.NodeStatus!.Name);
        Assert.Equal("ollama", probe.NodeStatus.Backend?.Name);
        Assert.Equal(4, probe.NodeStatus.Concurrency?.Limit);
        Assert.True(probe.NodeStatus.Gpu?.Cuda);
        Assert.Equal(new[] { "chat", "generate", "embed" }, probe.NodeStatus.Capabilities);
        Assert.False(probe.NodeStatus.Retrieval?.Enabled);
    }

    [Fact]
    public async Task ProbeAsync_reads_a_solo_node_with_retrieval_enabled()
    {
        // Recorded against a real solo node (inferhub-node:latest, retrieval enabled), 2026-09-05.
        // "rerank" is a string ("none"|"llm"), never a bool — caught only by driving a live node;
        // the first draft of this model (and this test) had it wrong as bool.
        var (client, _) = CreateClient(HttpStatusCode.OK, """
            {"mode":"solo","nodeVersion":"3.37.0","nowUtc":"2026-09-05T00:00:00Z","name":"local-node",
             "backend":{"name":"ollama","endpoint":"http://host.docker.internal:11434/","health":null},
             "concurrency":null,"gpu":{"cuda":false,"devices":0,"names":[]},
             "capabilities":["chat","embed"],
             "retrieval":{"enabled":true,"provider":"local","embeddingModel":"nomic-embed-text",
                          "mode":"vector","rerank":"none",
                          "collections":[{"name":"docs","dimension":8,"distance":"cosine","records":0}]},
             "models":[]}
            """);

        var probe = await client.ProbeAsync();

        var retrieval = probe.NodeStatus!.Retrieval!;
        Assert.True(retrieval.Enabled);
        Assert.Equal("local", retrieval.Provider);
        Assert.Equal("nomic-embed-text", retrieval.EmbeddingModel);
        Assert.Equal("vector", retrieval.RetrievalMode);
        Assert.Equal("none", retrieval.Rerank);
        var collection = Assert.Single(retrieval.Collections!);
        Assert.Equal("docs", collection.Name);
        Assert.Equal(0, collection.Records);
    }

    // ---- node-only endpoints ------------------------------------------------------------------

    [Fact]
    public async Task GetNodeVersionAsync_reads_the_version_field()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"version":"3.37.0"}""");

        Assert.Equal("3.37.0", await client.GetNodeVersionAsync());
        Assert.Equal("api/version", handler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'));
    }

    [Fact]
    public async Task GetNodeVersionAsync_against_a_hub_is_a_404()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, "");

        await Assert.ThrowsAsync<InferHubException>(() => client.GetNodeVersionAsync());
    }

    [Fact]
    public async Task ListNodeCollectionsAsync_returns_the_collections()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """
            {"collections":[{"name":"docs","dimension":768,"distance":"cosine","recordCount":412,"operations":890}]}
            """);

        var collections = await client.ListNodeCollectionsAsync();

        var collection = Assert.Single(collections);
        Assert.Equal("docs", collection.Name);
        Assert.Equal(768, collection.Dimension);
        Assert.Equal(412, collection.RecordCount);
        Assert.Equal("api/collections", handler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'));
    }

    [Fact]
    public async Task GetNodeCollectionAsync_returns_null_on_404()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"collection 'nope' does not exist"}""");

        Assert.Null(await client.GetNodeCollectionAsync("nope"));
    }

    [Fact]
    public async Task GetNodeCollectionAsync_returns_the_collection()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"name":"docs","dimension":768,"distance":"cosine","recordCount":412,"operations":890}""");

        var collection = await client.GetNodeCollectionAsync("docs");

        Assert.Equal("docs", collection!.Name);
        Assert.Equal("api/collections/docs", handler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'));
    }

    [Fact]
    public async Task CreateNodeCollectionAsync_posts_the_body_and_returns_the_collection()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"name":"docs","dimension":768,"distance":"cosine","recordCount":0,"operations":0}""");

        var collection = await client.CreateNodeCollectionAsync("docs", 768, "cosine");

        Assert.Equal("docs", collection.Name);
        Assert.Contains("\"name\":\"docs\"", handler.RequestBodies[0]);
        Assert.Contains("\"dimension\":768", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task CreateNodeCollectionAsync_surfaces_409_on_a_name_already_in_use()
    {
        var (client, _) = CreateClient(HttpStatusCode.Conflict, """{"error":"collection 'docs' already exists"}""");

        var error = await Assert.ThrowsAsync<InferHubException>(() => client.CreateNodeCollectionAsync("docs", 768));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }

    [Fact]
    public async Task DropNodeCollectionAsync_returns_true_when_dropped_and_false_on_404()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, """{"collection":"docs","dropped":true}""");
        Assert.True(await client.DropNodeCollectionAsync("docs"));

        var (missing, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"collection 'nope' does not exist"}""");
        Assert.False(await missing.DropNodeCollectionAsync("nope"));
    }

    // ---- the corrected 501 vs 503 vendor-capability split (hub 67 D4) -------------------------

    [Fact]
    public async Task Embed_against_a_backend_that_cannot_serve_it_is_a_permanent_501()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotImplemented, """
            {"error":"the 'anthropic' upstream this node runs does not serve 'embed'. Point Backend:Type at a backend that does, or send this request to one that has it."}
            """);

        var error = await Assert.ThrowsAsync<InferHubException>(
            () => client.EmbedAsync(Models.Ollama.EmbedRequest.FromText("nomic-embed-text", "hello")));

        Assert.Equal(HttpStatusCode.NotImplemented, error.StatusCode);
        Assert.Contains("does not serve 'embed'", error.Message);
        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public async Task Embed_against_a_capability_an_operator_disabled_is_a_temporary_503_with_retry_after()
    {
        var (client, handler) = CreateClient(HttpStatusCode.ServiceUnavailable, """
            {"error":"this node does not serve 'embed' (Node:Capabilities:Disabled)"}
            """);
        handler.ResponseHeaders["Retry-After"] = "30";

        var error = await Assert.ThrowsAsync<InferHubException>(
            () => client.EmbedAsync(Models.Ollama.EmbedRequest.FromText("nomic-embed-text", "hello")));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Contains("Capabilities:Disabled", error.Message);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }
}
