using System.Net;
using System.Text.Json;
using InferHub.Client.Configuration;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Ollama;
using InferHub.Client.Models.Vector;
using Microsoft.Extensions.DependencyInjection;

namespace InferHub.Client.Tests;

public class InferHubClientTests
{
    private static (InferHubClient Client, FakeHttpMessageHandler Handler) CreateClient(HttpStatusCode status, string body)
    {
        var handler = new FakeHttpMessageHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new InferHubClient(http), handler);
    }

    [Fact]
    public async Task ListModelsAsync_returns_models_on_200()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"models":[{"name":"llama3","digest":"abc","size":1234}]}""");

        var response = await client.ListModelsAsync();

        Assert.NotNull(response);
        Assert.Single(response.Models);
        Assert.Equal("llama3", response.Models[0].Name);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.EndsWith("api/tags", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task ChatAsync_forces_stream_false_and_deserializes_message()
    {
        const string body = """{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true,"eval_count":1}""";
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        var response = await client.ChatAsync(new ChatRequest
        {
            Model = "llama3",
            Stream = true,
            Messages = new[] { new ChatMessage { Role = "user", Content = "hi" } }
        });

        Assert.Equal("assistant", response.Message?.Role);
        Assert.Equal("hi", response.Message?.Content);
        Assert.True(response.Done);

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.False(sent.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task GenerateAsync_forces_stream_false_and_returns_response_text()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, """{"model":"llama3","response":"pong","done":true}""");

        var result = await client.GenerateAsync(new GenerateRequest { Model = "llama3", Prompt = "ping" });

        Assert.Equal("pong", result.Response);
    }

    [Fact]
    public async Task GetStatusAsync_deserializes_version_and_nodes()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, """
            {
              "coordinatorVersion": "2.0.0",
              "nowUtc": "2026-07-03T00:00:00Z",
              "uptimeSeconds": 42.5,
              "nodes": [ { "nodeId": "n1", "name": "alpha", "inFlight": 2 } ],
              "models": [ { "name": "llama3" } ]
            }
            """);

        var status = await client.GetStatusAsync();

        Assert.Equal("2.0.0", status.CoordinatorVersion);
        Assert.Equal(42.5, status.UptimeSeconds);
        Assert.Single(status.Nodes!);
        Assert.Equal("alpha", status.Nodes![0].Name);
    }

    [Fact]
    public async Task PingAsync_returns_true_on_success()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, "OK");
        Assert.True(await client.PingAsync());
    }

    [Fact]
    public async Task PingAsync_returns_false_on_non_success()
    {
        var (client, _) = CreateClient(HttpStatusCode.ServiceUnavailable, "down");
        Assert.False(await client.PingAsync());
    }

    [Fact]
    public async Task Chat_404_surfaces_InferHubException_with_error_body()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"model 'nope' not found"}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() =>
            client.ChatAsync(new ChatRequest { Model = "nope", Messages = Array.Empty<ChatMessage>() }));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("model 'nope' not found", ex.Message);
    }

    [Fact]
    public async Task Chat_401_surfaces_InferHubException()
    {
        var (client, _) = CreateClient(HttpStatusCode.Unauthorized, """{"error":"invalid api key"}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() =>
            client.ChatAsync(new ChatRequest { Model = "x" }));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Chat_400_uses_raw_body_when_no_error_field()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, "model is required");

        var ex = await Assert.ThrowsAsync<InferHubException>(() =>
            client.ChatAsync(new ChatRequest()));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("model is required", ex.Message);
    }

    [Fact]
    public void AddInferHubClient_registers_typed_client()
    {
        var services = new ServiceCollection();
        services.AddInferHubClient(o =>
        {
            o.BaseAddress = new Uri("http://localhost:5080");
            o.ApiKey = "abc";
        });

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IInferHubClient>();

        Assert.IsType<InferHubClient>(client);
    }

    [Fact]
    public void AddInferHubClient_throws_without_configure()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddInferHubClient(null!));
    }

    [Fact]
    public async Task Bearer_handler_attaches_api_key()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models":[]}""");
        var bearer = new InferHub.Client.Http.BearerAuthorizationHandler(new InferHubClientOptions { ApiKey = "secret-key" })
        {
            InnerHandler = handler
        };
        var http = new HttpClient(bearer) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubClient(http);

        await client.ListModelsAsync();

        var auth = handler.Requests[0].Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("secret-key", auth.Parameter);
    }

    [Fact]
    public async Task ChatStreamAsync_yields_chunks_and_stops_on_done()
    {
        var handler = new StreamingHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubClient(http);

        handler.EnqueueLine("""{"model":"llama3","message":{"role":"assistant","content":"hel"},"done":false}""");
        handler.EnqueueLine("""{"model":"llama3","message":{"role":"assistant","content":"lo"},"done":false}""");
        handler.EnqueueLine("""{"model":"llama3","message":{"role":"assistant","content":"!"},"done":true}""");
        handler.EnqueueLine("""{"model":"llama3","message":{"role":"assistant","content":"IGNORED"},"done":false}""");
        handler.Complete();

        var deltas = new List<string>();
        await foreach (var chunk in client.ChatStreamAsync(new ChatRequest { Model = "llama3" }))
        {
            deltas.Add(chunk.Message?.Content ?? string.Empty);
        }

        Assert.Equal(new[] { "hel", "lo", "!" }, deltas);

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.True(sent.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task ChatStreamAsync_surfaces_terminal_error_chunk_as_exception()
    {
        var handler = new StreamingHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubClient(http);

        handler.EnqueueLine("""{"model":"llama3","message":{"role":"assistant","content":"partial"},"done":false}""");
        handler.EnqueueLine("""{"error":"node dropped mid-stream","done":true}""");
        handler.Complete();

        var seen = new List<string?>();
        var ex = await Assert.ThrowsAsync<InferHubException>(async () =>
        {
            await foreach (var chunk in client.ChatStreamAsync(new ChatRequest { Model = "llama3" }))
            {
                seen.Add(chunk.Message?.Content);
            }
        });

        Assert.Equal("node dropped mid-stream", ex.Message);
        Assert.Single(seen);
        Assert.Equal("partial", seen[0]);
    }

    [Fact]
    public async Task GenerateStreamAsync_yields_response_deltas_and_stops_on_done()
    {
        var handler = new StreamingHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubClient(http);

        handler.EnqueueLine("""{"model":"llama3","response":"po","done":false}""");
        handler.EnqueueLine("""{"model":"llama3","response":"ng","done":true}""");
        handler.Complete();

        var deltas = new List<string>();
        await foreach (var chunk in client.GenerateStreamAsync(new GenerateRequest { Model = "llama3", Prompt = "ping" }))
        {
            deltas.Add(chunk.Response ?? string.Empty);
        }

        Assert.Equal(new[] { "po", "ng" }, deltas);
    }

    [Fact]
    public async Task ChatStreamAsync_cancellation_throws_promptly_between_chunks()
    {
        var handler = new StreamingHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubClient(http);

        using var cts = new CancellationTokenSource();
        handler.EnqueueLine("""{"model":"llama3","message":{"role":"assistant","content":"first"},"done":false}""");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in client.ChatStreamAsync(new ChatRequest { Model = "llama3" }, cts.Token))
            {
                cts.Cancel();
            }
        });
    }

    [Fact]
    public async Task EmbedAsync_batch_sends_string_array_input_and_returns_vectors()
    {
        const string body = """{"model":"nomic-embed-text","embeddings":[[0.1,0.2],[0.3,0.4]],"total_duration":123}""";
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        var response = await client.EmbedAsync(EmbedRequest.FromTexts("nomic-embed-text", new[] { "alpha", "beta" }));

        Assert.Equal("nomic-embed-text", response.Model);
        Assert.Equal(2, response.Embeddings.Length);
        Assert.Equal(new[] { 0.1f, 0.2f }, response.Embeddings[0]);
        Assert.Equal(new[] { 0.3f, 0.4f }, response.Embeddings[1]);
        Assert.Equal(123L, response.TotalDuration);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("api/embed", handler.Requests[0].RequestUri!.ToString());

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("nomic-embed-text", sent.GetProperty("model").GetString());
        var input = sent.GetProperty("input");
        Assert.Equal(JsonValueKind.Array, input.ValueKind);
        Assert.Equal(2, input.GetArrayLength());
        Assert.Equal("alpha", input[0].GetString());
        Assert.Equal("beta", input[1].GetString());
    }

    [Fact]
    public async Task EmbedAsync_single_string_sends_scalar_input()
    {
        const string body = """{"model":"nomic-embed-text","embeddings":[[0.5,0.6]]}""";
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        var response = await client.EmbedAsync(EmbedRequest.FromText("nomic-embed-text", "just one"));

        Assert.Single(response.Embeddings);
        Assert.Equal(new[] { 0.5f, 0.6f }, response.Embeddings[0]);

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        var input = sent.GetProperty("input");
        Assert.Equal(JsonValueKind.String, input.ValueKind);
        Assert.Equal("just one", input.GetString());
    }

    [Fact]
    public async Task EmbedAsync_empty_vector_list_throws_InferHubException()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, """{"model":"nomic-embed-text","embeddings":[]}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() =>
            client.EmbedAsync(EmbedRequest.FromText("nomic-embed-text", "x")));

        Assert.Contains("no vectors", ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_404_no_embedding_node_surfaces_typed_error()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"no node hosts model 'nomic-embed-text'"}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() =>
            client.EmbedAsync(EmbedRequest.FromText("nomic-embed-text", "x")));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("no node hosts model 'nomic-embed-text'", ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_400_bad_body_surfaces_typed_error()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, """{"error":"model is required"}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() =>
            client.EmbedAsync(EmbedRequest.FromText("", "x")));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("model is required", ex.Message);
    }

    [Fact]
    public async Task EmbedLegacyAsync_returns_single_vector()
    {
        const string body = """{"embedding":[0.11,0.22,0.33]}""";
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        var response = await client.EmbedLegacyAsync(new EmbeddingsRequest
        {
            Model = "nomic-embed-text",
            Prompt = "hello"
        });

        Assert.Equal(new[] { 0.11f, 0.22f, 0.33f }, response.Embedding);
        Assert.EndsWith("api/embeddings", handler.Requests[0].RequestUri!.ToString());

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("nomic-embed-text", sent.GetProperty("model").GetString());
        Assert.Equal("hello", sent.GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task EmbedLegacyAsync_empty_vector_throws()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, """{"embedding":[]}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() =>
            client.EmbedLegacyAsync(new EmbeddingsRequest { Model = "m", Prompt = "p" }));

        Assert.Contains("no vector", ex.Message);
    }

    [Fact]
    public async Task UpsertAsync_from_text_posts_body_and_returns_record()
    {
        const string body = """{"id":"doc-1","vector":[0.1,0.2],"metadata":{"kind":"doc"},"seqNo":7,"timestampUtc":"2026-07-06T00:00:00Z"}""";
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        var record = await client.UpsertAsync(
            "docs",
            VectorUpsert.FromText("doc-1", "hello world", "nomic-embed-text")
                .WithMetadata(new Dictionary<string, string> { ["kind"] = "doc" }));

        Assert.Equal("doc-1", record.Id);
        Assert.Equal(new[] { 0.1f, 0.2f }, record.Vector);
        Assert.Equal(7L, record.SeqNo);
        Assert.Equal("doc", record.Metadata!["kind"]);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("api/vector/docs/upsert", handler.Requests[0].RequestUri!.ToString());

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("doc-1", sent.GetProperty("id").GetString());
        Assert.Equal("hello world", sent.GetProperty("text").GetString());
        Assert.Equal("nomic-embed-text", sent.GetProperty("model").GetString());
        Assert.Equal("doc", sent.GetProperty("metadata").GetProperty("kind").GetString());
        Assert.False(sent.TryGetProperty("vector", out _));
    }

    [Fact]
    public async Task UpsertAsync_with_payload_serializes_payload_object()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"id":"a","vector":[1],"seqNo":1,"timestampUtc":"2026-07-06T00:00:00Z"}""");

        await client.UpsertAsync("docs", VectorUpsert.FromVector("a", new[] { 1f }).WithPayload(new { title = "t", n = 3 }));

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        var payload = sent.GetProperty("payload");
        Assert.Equal("t", payload.GetProperty("title").GetString());
        Assert.Equal(3, payload.GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task QueryAsync_returns_matches_from_envelope()
    {
        const string body = """{"matches":[{"id":"a","score":0.9,"metadata":{"k":"v"}},{"id":"b","score":0.4}]}""";
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        var matches = await client.QueryAsync("docs", VectorQuery.FromText("find me", k: 2));

        Assert.Equal(2, matches.Count);
        Assert.Equal("a", matches[0].Id);
        Assert.Equal(0.9, matches[0].Score);
        Assert.Equal("v", matches[0].Metadata!["k"]);
        Assert.EndsWith("api/vector/docs/query", handler.Requests[0].RequestUri!.ToString());

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("find me", sent.GetProperty("text").GetString());
        Assert.Equal(2, sent.GetProperty("k").GetInt32());
    }

    [Fact]
    public async Task RetrieveAsync_hits_retrieve_endpoint()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"matches":[]}""");

        var matches = await client.RetrieveAsync("docs", VectorQuery.FromVector(new[] { 0.1f, 0.2f }));

        Assert.Empty(matches);
        Assert.EndsWith("api/vector/docs/retrieve", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task QueryAsync_with_filter_sends_filter_object()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"matches":[]}""");

        await client.QueryAsync("docs", VectorQuery.FromText("q").WithFilter(new Dictionary<string, string> { ["lang"] = "en" }));

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("en", sent.GetProperty("filter").GetProperty("lang").GetString());
    }

    [Fact]
    public async Task GetRecordAsync_returns_record_and_reads_typed_payload()
    {
        const string body = """{"id":"doc-1","vector":[0.1],"payload":{"title":"hello"},"seqNo":3,"timestampUtc":"2026-07-06T00:00:00Z"}""";
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        var record = await client.GetRecordAsync("docs", "doc-1");

        Assert.NotNull(record);
        Assert.Equal("doc-1", record!.Id);
        Assert.EndsWith("api/vector/docs/doc-1", handler.Requests[0].RequestUri!.ToString());

        var payload = record.Payload.As<PayloadDto>();
        Assert.Equal("hello", payload!.Title);
    }

    [Fact]
    public async Task GetRecordAsync_returns_null_on_404()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"record 'x' not found"}""");

        Assert.Null(await client.GetRecordAsync("docs", "x"));
    }

    [Fact]
    public async Task DeleteRecordAsync_returns_true_on_success()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"id":"doc-1","deleted":true}""");

        Assert.True(await client.DeleteRecordAsync("docs", "doc-1"));
        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.EndsWith("api/vector/docs/doc-1", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task DeleteRecordAsync_returns_false_on_404()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"record 'x' not found"}""");

        Assert.False(await client.DeleteRecordAsync("docs", "x"));
    }

    [Fact]
    public async Task UpsertAsync_unknown_collection_surfaces_404()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, """{"error":"collection 'nope' not found"}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() =>
            client.UpsertAsync("nope", VectorUpsert.FromVector("a", new[] { 1f })));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("collection 'nope' not found", ex.Message);
    }

    [Fact]
    public async Task QueryAsync_missing_vector_and_text_surfaces_400()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, """{"error":"either 'vector' or 'text' must be provided"}""");

        var ex = await Assert.ThrowsAsync<InferHubException>(() =>
            client.QueryAsync("docs", new VectorQuery()));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public void PayloadExtensions_As_returns_default_for_absent_payload()
    {
        JsonElement? absent = null;
        Assert.Null(absent.As<PayloadDto>());
    }

    private sealed class PayloadDto
    {
        public string? Title { get; set; }
    }

    [Fact]
    public async Task ChatAsync_with_retrieval_options_sends_retrieve_headers()
    {
        const string body = """{"model":"llama3","message":{"role":"assistant","content":"grounded"},"done":true}""";
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        await client.ChatAsync(
            new ChatRequest { Model = "llama3", Messages = new[] { new ChatMessage { Role = "user", Content = "q" } } },
            new InferHubCallOptions
            {
                Retrieval = new RetrievalOptions("docs") { K = 5, Model = "nomic-embed-text" },
                ConversationId = "conv-1"
            });

        var headers = handler.Requests[0].Headers;
        Assert.Equal("docs", headers.GetValues("X-InferHub-Retrieve").Single());
        Assert.Equal("5", headers.GetValues("X-InferHub-Retrieve-K").Single());
        Assert.Equal("nomic-embed-text", headers.GetValues("X-InferHub-Retrieve-Model").Single());
        Assert.Equal("conv-1", headers.GetValues("X-InferHub-Conversation").Single());
    }

    [Fact]
    public async Task ChatAsync_without_options_sends_no_inferhub_headers()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true}""");

        await client.ChatAsync(new ChatRequest { Model = "llama3" });

        var headers = handler.Requests[0].Headers;
        Assert.False(headers.Contains("X-InferHub-Retrieve"));
        Assert.False(headers.Contains("X-InferHub-Conversation"));
    }

    [Fact]
    public async Task ChatAsync_omits_optional_retrieve_headers_when_null()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true}""");

        await client.ChatAsync(new ChatRequest { Model = "llama3" }, InferHubCallOptions.ForRetrieval("docs"));

        var headers = handler.Requests[0].Headers;
        Assert.Equal("docs", headers.GetValues("X-InferHub-Retrieve").Single());
        Assert.False(headers.Contains("X-InferHub-Retrieve-K"));
        Assert.False(headers.Contains("X-InferHub-Retrieve-Model"));
    }

    [Fact]
    public async Task ChatAsync_parses_source_ids_from_response_header()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"model":"llama3","message":{"role":"assistant","content":"a"},"done":true}""");
        handler.ResponseHeaders["X-InferHub-Sources"] = """["doc-1","doc-2"]""";

        var response = await client.ChatAsync(new ChatRequest { Model = "llama3" }, InferHubCallOptions.ForRetrieval("docs"));

        Assert.Equal(new[] { "doc-1", "doc-2" }, response.SourceIds);
    }

    [Fact]
    public async Task ChatAsync_source_ids_null_when_header_absent()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, """{"model":"llama3","message":{"role":"assistant","content":"a"},"done":true}""");

        var response = await client.ChatAsync(new ChatRequest { Model = "llama3" });

        Assert.Null(response.SourceIds);
    }

    [Fact]
    public async Task GenerateAsync_with_retrieval_returns_source_ids()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"model":"llama3","response":"grounded","done":true}""");
        handler.ResponseHeaders["X-InferHub-Sources"] = """["r1"]""";

        var response = await client.GenerateAsync(
            new GenerateRequest { Model = "llama3", Prompt = "q" },
            InferHubCallOptions.ForRetrieval("docs", k: 3));

        Assert.Equal(new[] { "r1" }, response.SourceIds);
        Assert.Equal("3", handler.Requests[0].Headers.GetValues("X-InferHub-Retrieve-K").Single());
    }

    [Fact]
    public async Task ChatAsync_424_surfaces_InferHubRetrievalException()
    {
        var (client, _) = CreateClient((HttpStatusCode)424, """{"error":"retrieval unavailable"}""");

        var ex = await Assert.ThrowsAsync<InferHubRetrievalException>(() =>
            client.ChatAsync(new ChatRequest { Model = "llama3" }, InferHubCallOptions.ForRetrieval("docs")));

        Assert.Equal(HttpStatusCode.FailedDependency, ex.StatusCode);
        Assert.Equal("retrieval unavailable", ex.Message);
        Assert.IsAssignableFrom<InferHubException>(ex);
    }

    [Fact]
    public async Task ChatAsync_blank_retrieval_collection_throws_before_send()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """{"model":"llama3","message":{"role":"assistant","content":"x"},"done":true}""");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ChatAsync(new ChatRequest { Model = "llama3" }, new InferHubCallOptions { Retrieval = new RetrievalOptions("  ") }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ChatStreamAsync_with_retrieval_sends_headers_and_stamps_source_ids_on_chunks()
    {
        var handler = new StreamingHttpMessageHandler();
        handler.ResponseHeaders["X-InferHub-Sources"] = """["doc-7","doc-8"]""";
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubClient(http);

        handler.EnqueueLine("""{"model":"llama3","message":{"role":"assistant","content":"grn"},"done":false}""");
        handler.EnqueueLine("""{"model":"llama3","message":{"role":"assistant","content":"ded"},"done":true}""");
        handler.Complete();

        var sources = new List<IReadOnlyList<string>?>();
        await foreach (var chunk in client.ChatStreamAsync(
            new ChatRequest { Model = "llama3" },
            InferHubCallOptions.ForRetrieval("docs", k: 2)))
        {
            sources.Add(chunk.SourceIds);
        }

        Assert.Equal(2, sources.Count);
        Assert.All(sources, s => Assert.Equal(new[] { "doc-7", "doc-8" }, s));
        Assert.Equal("docs", handler.Requests[0].Headers.GetValues("X-InferHub-Retrieve").Single());
        Assert.Equal("2", handler.Requests[0].Headers.GetValues("X-InferHub-Retrieve-K").Single());
    }

    [Fact]
    public async Task Bearer_handler_leaves_authorization_off_when_no_key()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models":[]}""");
        var bearer = new InferHub.Client.Http.BearerAuthorizationHandler(new InferHubClientOptions())
        {
            InnerHandler = handler
        };
        var http = new HttpClient(bearer) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubClient(http);

        await client.ListModelsAsync();

        Assert.Null(handler.Requests[0].Headers.Authorization);
    }
}
