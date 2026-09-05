using System.Net;
using System.Text.Json;
using InferHub.Client.Exceptions;
using InferHub.Client.Models.Corpus;
using InferHub.Client.Models.Images;
using InferHub.Client.Models.Node;
using InferHub.Client.Models.Ollama;
using InferHub.Client.Models.OpenAi;

namespace InferHub.Client.Tests;

/// <summary>
/// Phase 15 — drives <c>conformance/cases.json</c> against this client. The corpus is data and no
/// language owns it; this file is the C# reader (~150 lines, per <c>conformance/README.md</c>'s
/// budget), one <c>switch</c> per case <c>kind</c> rather than per case, so a new case needs a new
/// JSON entry, not a new test method.
/// </summary>
public class ConformanceCorpusTests
{
    private static readonly JsonElement Cases = LoadCases();

    public static IEnumerable<object[]> CaseIds()
        => Cases.EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task Case(string id)
    {
        var kase = Cases.EnumerateArray().First(c => c.GetProperty("id").GetString() == id);
        var response = kase.GetProperty("response");
        var status = (HttpStatusCode)response.GetProperty("status").GetInt32();
        var body = response.GetProperty("body").GetString() ?? string.Empty;
        var mediaType = response.TryGetProperty("mediaType", out var mt) ? mt.GetString()! : "application/json";

        var handler = new FakeHttpMessageHandler(status, body, mediaType);
        if (response.TryGetProperty("headers", out var headers))
        {
            foreach (var h in headers.EnumerateObject())
            {
                handler.ResponseHeaders[h.Name] = h.Value.GetString()!;
            }
        }

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        var assert = kase.GetProperty("assert");
        var assertKind = assert.GetProperty("kind").GetString()!;

        switch (kase.GetProperty("kind").GetString())
        {
            case "probe":
                await RunProbe(new InferHubClient(http), assert, assertKind);
                break;
            case "chat":
                await RunChat(new InferHubClient(http), assert, assertKind);
                break;
            case "chat-stream":
                await RunChatStream(new InferHubClient(http), assert);
                break;
            case "openai-chat":
                await RunOpenAiChat(new InferHubOpenAiClient(http), assert);
                break;
            case "openai-chat-stream":
                await RunOpenAiChatStream(new InferHubOpenAiClient(http), assert);
                break;
            case "openai-images-submit":
                await RunImagesSubmit(new InferHubImagesClient(http), assert);
                break;
            case "ingest-text":
                await RunIngestText(new InferHubCorpusClient(http), assert);
                break;
            case "search":
                await RunSearch(new InferHubCorpusClient(http), assert);
                break;
            case "chunks":
                await RunChunks(new InferHubCorpusClient(http), assert);
                break;
            default:
                throw new NotSupportedException($"case '{id}': no C# runner for kind '{kase.GetProperty("kind").GetString()}'");
        }
    }

    private static async Task RunProbe(InferHubClient client, JsonElement assert, string assertKind)
    {
        var probe = await client.ProbeAsync();
        if (assertKind == "solo-node")
        {
            Assert.Equal(InferHubTargetKind.SoloNode, probe.Kind);
            Assert.Equal(assert.GetProperty("nodeName").GetString(), probe.NodeStatus!.Name);
            Assert.Equal(assert.GetProperty("retrievalRerank").GetString(), probe.NodeStatus.Retrieval!.Rerank);
        }
        else if (assertKind == "hub")
        {
            Assert.Equal(InferHubTargetKind.Hub, probe.Kind);
            Assert.Equal(assert.GetProperty("nodeCount").GetInt32(), probe.HubStatus!.Nodes!.Count);
        }
    }

    private static async Task RunChat(InferHubClient client, JsonElement assert, string assertKind)
    {
        if (assertKind == "throws-retrieval-exception")
        {
            await Assert.ThrowsAsync<InferHubRetrievalException>(() => client.ChatAsync(new ChatRequest { Model = "llama3" }));
            return;
        }

        if (assertKind == "source-ids")
        {
            var result = await client.ChatAsync(new ChatRequest { Model = "llama3" });
            var expected = assert.GetProperty("expected").EnumerateArray().Select(e => e.GetString()!).ToArray();
            Assert.Equal(expected, result.SourceIds);
            return;
        }

        throw new NotSupportedException(assertKind);
    }

    private static async Task RunChatStream(InferHubClient client, JsonElement assert)
    {
        var seen = new List<ChatResponse>();
        var ex = await Assert.ThrowsAsync<InferHubException>(async () =>
        {
            await foreach (var chunk in client.ChatStreamAsync(new ChatRequest { Model = "llama3" }))
            {
                seen.Add(chunk);
            }
        });

        Assert.Equal(assert.GetProperty("partialChunks").GetInt32(), seen.Count);
        Assert.Equal(assert.GetProperty("errorMessage").GetString(), ex.Message);
    }

    private static async Task RunOpenAiChat(InferHubOpenAiClient client, JsonElement assert)
    {
        var ex = await Assert.ThrowsAsync<InferHubOpenAiException>(
            () => client.CreateChatCompletionAsync(new ChatCompletionRequest { Model = "gemma:2b" }));

        AssertOpenAiError(ex, assert);
    }

    private static async Task RunOpenAiChatStream(InferHubOpenAiClient client, JsonElement assert)
    {
        ChatCompletionChunk? usageFrame = null;
        await foreach (var chunk in client.StreamChatCompletionAsync(new ChatCompletionRequest { Model = "gemma:2b" }))
        {
            if (chunk.Choices.Count == 0)
            {
                usageFrame = chunk;
            }
        }

        Assert.NotNull(usageFrame);
        Assert.Equal(assert.GetProperty("promptTokens").GetInt32(), usageFrame!.Usage?.PromptTokens);
        Assert.Equal(assert.GetProperty("totalTokens").GetInt32(), usageFrame.Usage?.TotalTokens);
    }

    private static async Task RunImagesSubmit(InferHubImagesClient client, JsonElement assert)
    {
        var ex = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.SubmitAsync(new ImageGenerationRequest
        {
            Model = "llava:latest",
            Prompt = "a lighthouse in fog"
        }));

        AssertOpenAiError(ex, assert);
    }

    private static async Task RunIngestText(InferHubCorpusClient client, JsonElement assert)
    {
        var result = await client.IngestTextAsync("handbook", new TextDocument { Text = "irrelevant — the recorded response decides the outcome" });

        Assert.True(result.IsPartial);
        Assert.Equal(assert.GetProperty("documentId").GetString(), result.DocumentId);
        Assert.Equal(assert.GetProperty("chunksEmbedded").GetInt32(), result.ChunksEmbedded);
    }

    private static async Task RunSearch(InferHubCorpusClient client, JsonElement assert)
    {
        var result = await client.SearchAsync("handbook", "how do I get an expense approved");

        Assert.Equal(assert.GetProperty("firstDocumentId").GetString(), result.Hits[0].DocumentId);
        Assert.Equal(assert.GetProperty("secondDocumentId").GetString(), result.Hits[1].DocumentId);
        // The point of the case: hits[0].Score < hits[1].Score is allowed. A client must not have
        // silently re-sorted them into descending score order.
    }

    private static async Task RunChunks(InferHubCorpusClient client, JsonElement assert)
    {
        var chunks = await client.GetChunksAsync("handbook", "onboarding");

        Assert.Equal(assert.GetProperty("expected").GetString(), chunks[0].Index);
    }

    private static void AssertOpenAiError(InferHubOpenAiException ex, JsonElement assert)
    {
        if (assert.TryGetProperty("errorType", out var type))
        {
            Assert.Equal(type.GetString(), ex.ErrorType);
        }

        if (assert.TryGetProperty("param", out var param))
        {
            if (param.ValueKind == JsonValueKind.Null) Assert.Null(ex.Param); else Assert.Equal(param.GetString(), ex.Param);
        }

        if (assert.TryGetProperty("code", out var code))
        {
            if (code.ValueKind == JsonValueKind.Null) Assert.Null(ex.ErrorCode); else Assert.Equal(code.GetString(), ex.ErrorCode);
        }

        if (assert.TryGetProperty("errorCode", out var errorCode))
        {
            Assert.Equal(errorCode.GetString(), ex.ErrorCode);
        }

        if (assert.TryGetProperty("retryAfterSeconds", out var retryAfter))
        {
            Assert.Equal(TimeSpan.FromSeconds(retryAfter.GetInt32()), ex.RetryAfter);
        }
    }

    private static JsonElement LoadCases()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindCasesFile()));
        return doc.RootElement.GetProperty("cases").Clone();
    }

    /// <summary>
    /// Walks up from the test binary's output directory to find the repo-root <c>conformance/</c>
    /// folder — the corpus is data shared by every language's test project, not copied into each.
    /// </summary>
    private static string FindCasesFile()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "conformance", "cases.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"conformance/cases.json not found above {AppContext.BaseDirectory}");
    }
}
