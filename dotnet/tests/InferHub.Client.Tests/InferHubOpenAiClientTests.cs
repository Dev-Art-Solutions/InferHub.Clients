using System.Net;
using System.Text.Json;
using InferHub.Client.Exceptions;
using InferHub.Client.Models.OpenAi;

namespace InferHub.Client.Tests;

/// <summary>
/// The OpenAI dialect (<c>/v1/*</c>), phase 8. <b>Every body in this file was recorded from a real
/// hub</b> — InferHub 3.37.0 with one node driving Ollama — rather than typed from the hub's
/// source. Two are marked where that is not true, and each says why.
/// </summary>
public class InferHubOpenAiClientTests
{
    private const string RecordedChatCompletion = """
        {"id":"chatcmpl-a4dfc67729e94d749f21929cea76553f","created":1788036515,"model":"gemma:2b","choices":[{"index":0,"message":{"role":"assistant","content":"Hello!"},"finish_reason":"stop","logprobs":null}],"usage":{"prompt_tokens":27,"completion_tokens":16,"total_tokens":43},"object":"chat.completion"}
        """;

    // One streamed answer, verbatim: the opening frame carrying role, two content frames, the
    // terminal frame with an empty delta and finish_reason, the usage frame with NO choices, and
    // the sentinel. The blank line between frames is the frame separator, not formatting.
    private const string RecordedChatStream =
        "data: {\"id\":\"chatcmpl-cdf9c\",\"created\":1788036525,\"model\":\"gemma:2b\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Hi\"},\"finish_reason\":null,\"logprobs\":null}],\"object\":\"chat.completion.chunk\"}\n\n"
        + "data: {\"id\":\"chatcmpl-cdf9c\",\"created\":1788036525,\"model\":\"gemma:2b\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"!\"},\"finish_reason\":null,\"logprobs\":null}],\"object\":\"chat.completion.chunk\"}\n\n"
        + "data: {\"id\":\"chatcmpl-cdf9c\",\"created\":1788036525,\"model\":\"gemma:2b\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\",\"logprobs\":null}],\"object\":\"chat.completion.chunk\"}\n\n"
        + "data: {\"id\":\"chatcmpl-cdf9c\",\"created\":1788036525,\"model\":\"gemma:2b\",\"choices\":[],\"usage\":{\"prompt_tokens\":24,\"completion_tokens\":8,\"total_tokens\":32},\"object\":\"chat.completion.chunk\"}\n\n"
        + "data: [DONE]\n\n";

    private const string RecordedCompletion = """
        {"id":"cmpl-84a01c9bf60c4ce3bebeb54fa74869f7","created":1788036550,"model":"gemma:2b","choices":[{"index":0,"text":"The answer is not 2","finish_reason":"stop","logprobs":null}],"usage":{"prompt_tokens":26,"completion_tokens":6,"total_tokens":32},"object":"text_completion"}
        """;

    private const string RecordedCompletionStream =
        "data: {\"id\":\"cmpl-d8114\",\"created\":1788036555,\"model\":\"gemma:2b\",\"choices\":[{\"index\":0,\"text\":\"I\",\"finish_reason\":null,\"logprobs\":null}],\"object\":\"text_completion\"}\n\n"
        + "data: {\"id\":\"cmpl-d8114\",\"created\":1788036555,\"model\":\"gemma:2b\",\"choices\":[{\"index\":0,\"text\":\" am\",\"finish_reason\":null,\"logprobs\":null}],\"object\":\"text_completion\"}\n\n"
        + "data: [DONE]\n\n";

    // The steer refusal, verbatim: one sentence for an unknown provider, a disabled one and a real
    // one that maps a different model — and note code is null, not an empty string.
    private const string RecordedSteerRefusal = """
        {"error":{"message":"no provider 'openrouter' serves model 'gemma:2b' on this hub. The X-InferHub-Provider header can only choose among the providers already configured for a model; 'node' keeps the request on the fleet.","type":"invalid_request_error","param":"model","code":null}}
        """;

    private const string RecordedModelNotFound = """
        {"error":{"message":"model 'nope:404' not found","type":"not_found_error","param":"model","code":"model_not_found"}}
        """;

    private const string RecordedModelList = """
        {"data":[{"id":"all-minilm:latest","created":1788036550,"owned_by":"inferhub","capabilities":["chat","embed"],"object":"model"},{"id":"gemma:2b","created":1788036550,"owned_by":"inferhub","capabilities":["chat","embed"],"object":"model"}],"object":"list"}
        """;

    private static (InferHubOpenAiClient Client, FakeHttpMessageHandler Handler) CreateClient(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json")
    {
        var handler = new FakeHttpMessageHandler(status, body, mediaType);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new InferHubOpenAiClient(http), handler);
    }

    [Fact]
    public async Task CreateChatCompletionAsync_posts_v1_path_and_forces_stream_false()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedChatCompletion);

        var answer = await client.CreateChatCompletionAsync(new ChatCompletionRequest
        {
            Model = "gemma:2b",
            Stream = true,
            Messages = new[] { ChatCompletionMessage.User("hi") }
        });

        Assert.EndsWith("v1/chat/completions", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("Hello!", answer.Choices[0].Message?.Content);
        Assert.Equal("stop", answer.Choices[0].FinishReason);
        Assert.Equal(43, answer.Usage?.TotalTokens);

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.False(sent.GetProperty("stream").GetBoolean());
        Assert.Equal("hi", sent.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CreateChatCompletionAsync_surfaces_served_by_and_sources()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedChatCompletion);
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node";
        handler.ResponseHeaders["X-InferHub-Sources"] = """["doc-1","doc-2"]""";

        var answer = await client.CreateChatCompletionAsync(
            new ChatCompletionRequest { Model = "gemma:2b" },
            InferHubCallOptions.ForRetrieval("handbook", k: 3));

        Assert.Equal("node", answer.ServedBy);
        Assert.Equal(new[] { "doc-1", "doc-2" }, answer.SourceIds);
        Assert.Equal("handbook", handler.Requests[0].Headers.GetValues("X-InferHub-Retrieve").Single());
        Assert.Equal("3", handler.Requests[0].Headers.GetValues("X-InferHub-Retrieve-K").Single());
    }

    [Fact]
    public async Task CreateChatCompletionAsync_leaves_served_by_null_when_the_header_is_absent()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedChatCompletion);

        var answer = await client.CreateChatCompletionAsync(new ChatCompletionRequest { Model = "gemma:2b" });

        Assert.Null(answer.ServedBy);
    }

    [Fact]
    public async Task StreamChatCompletionAsync_yields_every_frame_and_stops_at_the_sentinel()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedChatStream, "text/event-stream");
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node";

        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in client.StreamChatCompletionAsync(new ChatCompletionRequest { Model = "gemma:2b" }))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(4, chunks.Count);
        Assert.Equal("assistant", chunks[0].Choices[0].Delta?.Role);
        Assert.Equal("Hi", chunks[0].Choices[0].Delta?.Content);
        Assert.Equal("!", chunks[1].Choices[0].Delta?.Content);
        Assert.Equal("stop", chunks[2].Choices[0].FinishReason);
        Assert.All(chunks, chunk => Assert.Equal("node", chunk.ServedBy));

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.True(sent.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task StreamChatCompletionAsync_yields_the_usage_frame_rather_than_skipping_it()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedChatStream, "text/event-stream");

        ChatCompletionChunk? last = null;
        await foreach (var chunk in client.StreamChatCompletionAsync(new ChatCompletionRequest
        {
            Model = "gemma:2b",
            StreamOptions = new ChatCompletionStreamOptions { IncludeUsage = true }
        }))
        {
            last = chunk;
        }

        // Empty choices, and the only token counts a streamed call ever reports.
        Assert.NotNull(last);
        Assert.Empty(last!.Choices);
        Assert.Equal(24, last.Usage?.PromptTokens);
        Assert.Equal(32, last.Usage?.TotalTokens);
    }

    [Fact]
    public async Task StreamChatCompletionAsync_ends_quietly_when_the_stream_stops_without_the_sentinel()
    {
        // A node that dropped mid-answer: the hub sends its truncation frame with
        // finish_reason=stop and the connection ends. The caller keeps the partial answer.
        const string truncated =
            "data: {\"id\":\"chatcmpl-x\",\"created\":1,\"model\":\"gemma:2b\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Hi\"},\"finish_reason\":null}],\"object\":\"chat.completion.chunk\"}\n\n"
            + "data: {\"id\":\"chatcmpl-x\",\"created\":1,\"model\":\"gemma:2b\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"object\":\"chat.completion.chunk\"}\n\n";

        var (client, _) = CreateClient(HttpStatusCode.OK, truncated, "text/event-stream");

        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in client.StreamChatCompletionAsync(new ChatCompletionRequest { Model = "gemma:2b" }))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(2, chunks.Count);
        Assert.Equal("stop", chunks[1].Choices[0].FinishReason);
    }

    [Fact]
    public async Task StreamChatCompletionAsync_throws_on_a_malformed_frame()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, "data: {not json}\n\n", "text/event-stream");

        var ex = await Assert.ThrowsAsync<InferHubException>(async () =>
        {
            await foreach (var _ in client.StreamChatCompletionAsync(new ChatCompletionRequest { Model = "gemma:2b" }))
            {
            }
        });

        Assert.Contains("Malformed SSE frame", ex.Message);
    }

    [Fact]
    public async Task CreateCompletionAsync_posts_the_legacy_path()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedCompletion);
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node";

        var answer = await client.CreateCompletionAsync(CompletionRequest.FromText("gemma:2b", "1+1="));

        Assert.EndsWith("v1/completions", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("The answer is not 2", answer.Choices[0].Text);
        Assert.Equal("node", answer.ServedBy);
    }

    [Fact]
    public async Task StreamCompletionAsync_yields_one_response_per_frame()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, RecordedCompletionStream, "text/event-stream");

        var text = string.Empty;
        await foreach (var chunk in client.StreamCompletionAsync(CompletionRequest.FromText("gemma:2b", "1+1=")))
        {
            text += chunk.Choices[0].Text;
        }

        Assert.Equal("I am", text);
    }

    [Fact]
    public async Task CreateEmbeddingsAsync_reads_a_float_encoded_vector()
    {
        const string body = """
            {"model":"all-minilm:latest","data":[{"index":0,"embedding":[-0.06283187,0.05488413,0.0520008],"object":"embedding"}],"usage":{"prompt_tokens":3,"total_tokens":3},"object":"list"}
            """;
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        var answer = await client.CreateEmbeddingsAsync(OpenAiEmbeddingsRequest.FromText("all-minilm:latest", "hello"));

        Assert.EndsWith("v1/embeddings", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(-0.06283187, answer.Data[0].AsFloats()[0], 6);
        Assert.Equal(3, answer.Data[0].AsFloats().Length);
    }

    [Fact]
    public async Task CreateEmbeddingsAsync_decodes_a_base64_vector_to_the_same_numbers()
    {
        // The first three floats of the vector above, as the same hub returned them under
        // encoding_format: base64 — little-endian float32, which is what the OpenAI Python SDK
        // asks for by default and therefore the common case rather than the exotic one.
        const string body = """
            {"model":"all-minilm:latest","data":[{"index":0,"embedding":"/62AvS7OYD3K/lQ9","object":"embedding"}],"usage":{"prompt_tokens":3,"total_tokens":3},"object":"list"}
            """;
        var (client, handler) = CreateClient(HttpStatusCode.OK, body);

        var answer = await client.CreateEmbeddingsAsync(new OpenAiEmbeddingsRequest
        {
            Model = "all-minilm:latest",
            EncodingFormat = "base64",
            Input = JsonDocument.Parse("\"hello\"").RootElement
        });

        var vector = answer.Data[0].AsFloats();
        Assert.Equal(3, vector.Length);
        Assert.Equal(-0.06283187, vector[0], 6);
        Assert.Equal(0.05488413, vector[1], 6);
        Assert.Equal(0.0520008, vector[2], 6);

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("base64", sent.GetProperty("encoding_format").GetString());
    }

    [Fact]
    public async Task CreateEmbeddingsAsync_throws_when_the_answer_carries_no_vectors()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, """{"model":"all-minilm:latest","data":[],"object":"list"}""");

        await Assert.ThrowsAsync<InferHubException>(
            () => client.CreateEmbeddingsAsync(OpenAiEmbeddingsRequest.FromText("all-minilm:latest", "hello")));
    }

    [Fact]
    public async Task A_refused_provider_steer_surfaces_the_envelope_rather_than_the_raw_body()
    {
        var (client, handler) = CreateClient(HttpStatusCode.BadRequest, RecordedSteerRefusal);

        var ex = await Assert.ThrowsAsync<InferHubOpenAiException>(
            () => client.CreateChatCompletionAsync(
                new ChatCompletionRequest { Model = "gemma:2b" },
                InferHubCallOptions.ForProvider("openrouter")));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.StartsWith("no provider 'openrouter' serves model 'gemma:2b'", ex.Message);
        Assert.Equal("invalid_request_error", ex.ErrorType);
        Assert.Equal("model", ex.Param);
        Assert.Null(ex.ErrorCode);
        Assert.Equal("openrouter", handler.Requests[0].Headers.GetValues("X-InferHub-Provider").Single());
    }

    [Fact]
    public async Task A_missing_model_surfaces_type_and_code()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedModelNotFound);

        var ex = await Assert.ThrowsAsync<InferHubOpenAiException>(
            () => client.CreateChatCompletionAsync(new ChatCompletionRequest { Model = "nope:404" }));

        Assert.Equal("not_found_error", ex.ErrorType);
        Assert.Equal("model_not_found", ex.ErrorCode);
        Assert.Equal("model 'nope:404' not found", ex.Message);
    }

    [Fact]
    public async Task An_error_code_that_arrived_as_a_number_is_read_as_a_string()
    {
        // NOT recorded from this hub: it is the shape a passed-through upstream sends (OpenAI
        // writes "rate_limit_exceeded", OpenRouter writes 429), which the hub keeps parseable on
        // its own side for the same reason. Reproducing it needs a configured provider and a real
        // rate limit, so this body is constructed and said so.
        const string body = """
            {"error":{"message":"rate limited upstream","type":"rate_limit_error","param":null,"code":429}}
            """;
        var (client, _) = CreateClient(HttpStatusCode.TooManyRequests, body);

        var ex = await Assert.ThrowsAsync<InferHubOpenAiException>(
            () => client.CreateChatCompletionAsync(new ChatCompletionRequest { Model = "gemma:2b" }));

        Assert.Equal("429", ex.ErrorCode);
        Assert.Equal("rate limited upstream", ex.Message);
    }

    [Fact]
    public async Task A_424_stays_the_retrieval_exception_in_this_dialect_too()
    {
        // The /v1 chat handler answers retrieval failure in its own envelope, with the same 424 the
        // Ollama surface uses. One condition, one exception type, whichever dialect asked.
        const string body = """
            {"error":{"message":"vector store is disabled; retrieval header cannot be honoured","type":"api_error","param":null,"code":"retrieval_unavailable"}}
            """;
        var (client, _) = CreateClient(HttpStatusCode.FailedDependency, body);

        var ex = await Assert.ThrowsAsync<InferHubRetrievalException>(
            () => client.CreateChatCompletionAsync(
                new ChatCompletionRequest { Model = "gemma:2b" },
                InferHubCallOptions.ForRetrieval("handbook")));

        Assert.StartsWith("vector store is disabled", ex.Message);
    }

    [Fact]
    public async Task ListModelsAsync_returns_the_capabilities_extension()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedModelList);

        var models = await client.ListModelsAsync();

        Assert.EndsWith("v1/models", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(2, models.Count);
        Assert.Equal("all-minilm:latest", models[0].Id);
        Assert.Equal(new[] { "chat", "embed" }, models[0].Capabilities);
        Assert.Equal("inferhub", models[0].OwnedBy);
    }

    [Fact]
    public async Task GetModelAsync_returns_null_for_a_model_the_hub_does_not_serve()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, """
            {"error":{"message":"model 'nope-does-not-exist' not found","type":"not_found_error","param":"model","code":"model_not_found"}}
            """);

        Assert.Null(await client.GetModelAsync("nope-does-not-exist"));
    }

    [Fact]
    public async Task The_fleet_only_steer_sends_the_node_value()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedChatCompletion);

        await client.CreateChatCompletionAsync(
            new ChatCompletionRequest { Model = "gemma:2b" },
            InferHubCallOptions.ForFleetOnly());

        Assert.Equal("node", handler.Requests[0].Headers.GetValues("X-InferHub-Provider").Single());
    }

    [Fact]
    public async Task Steering_to_a_provider_and_to_the_fleet_at_once_is_rejected_before_the_request_leaves()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedChatCompletion);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CreateChatCompletionAsync(
                new ChatCompletionRequest { Model = "gemma:2b" },
                new InferHubCallOptions { Provider = "openrouter", FleetOnly = true }));

        Assert.Empty(handler.Requests);
    }
}
