using System.Net;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Jobs;
using InferHub.Client.Models.Ollama;
using Microsoft.Extensions.DependencyInjection;

namespace InferHub.Client.Tests;

/// <summary>
/// Text jobs (<c>/api/jobs</c>, hub v3.103+).
/// </summary>
/// <remarks>
/// The status bodies and the result's header are <b>recorded</b> from a real coordinator and a real
/// node over Ollama (<c>qwen2.5:0.5b</c>) on 2026-11-16. The <c>group</c> body is <b>derived</b> from
/// the hub's serializer (<c>JobEndpoints.Describe</c>) — no split-model group was running when
/// these were recorded — and so is the SSE framing.
/// </remarks>
public class InferHubJobsClientTests
{
    private const string Id = "8755d357-7f7a-493b-984b-09eeb1c3797e";

    private const string RecordedQueued = """
        {"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b","status":"queued","stage":"waiting","createdAt":"2026-11-16T11:40:16.2677184+00:00","updatedAt":"2026-11-16T11:40:16.2677184+00:00","attempts":0,"pieces":0}
        """;

    private const string RecordedPreparing = """
        {"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b","status":"running","stage":"preparing","createdAt":"2026-11-16T11:40:16.2677184+00:00","startedAt":"2026-11-16T11:40:16.2950626+00:00","updatedAt":"2026-11-16T11:40:18.3304721+00:00","node":{"id":"d2b53940-e3b4-4b37-855d-536e9a4fe10e","name":"live150-node","reportedAt":"2026-11-16T11:40:18.3304721+00:00","pieces":0},"attempts":1,"pieces":0}
        """;

    private const string RecordedSucceeded = """
        {"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b","status":"succeeded","stage":"done","createdAt":"2026-11-16T11:40:16.2677184+00:00","startedAt":"2026-11-16T11:40:16.2950626+00:00","updatedAt":"2026-11-16T11:40:33.342876+00:00","completedAt":"2026-11-16T11:40:33.342876+00:00","node":{"id":"d2b53940-e3b4-4b37-855d-536e9a4fe10e","name":"live150-node","reportedAt":"2026-11-16T11:40:32.4983246+00:00","pieces":1},"attempts":1,"pieces":300,"tokens":300,"tokensPerSecond":340.23}
        """;

    private const string RecordedPartialResult = """
        {"model":"qwen2.5:0.5b","message":{"role":"assistant","content":""},"done":false}
        """;

    private const string RecordedResult = """
        {"model":"qwen2.5:0.5b","created_at":"2026-11-16T11:40:33.3396733Z","message":{"role":"assistant","content":"Ah, the lighthouse keeper."},"done":true,"done_reason":"length","eval_count":300}
        """;

    private const string DerivedWithGroup = """
        {"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"wizardlm-70b-split","status":"running","stage":"preparing","detail":"group starting: 18420 of 25022 MiB sent to its members","attempts":1,"pieces":0,"group":{"name":"wizardlm-70b-split","head":"shard-a","ready":false,"state":"starting","members":[{"node":"shard-a","layers":27},{"node":"shard-b","layers":27,"expectMiB":12511,"sentMiB":9210},{"node":"shard-c","layers":27,"expectMiB":12511,"sentMiB":9210}]}}
        """;

    private const string DerivedFailed = """
        {"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"llama3","status":"failed","stage":"done","attempts":1,"pieces":4,"error":{"status":502,"code":"node_lost","message":"node 'gpu-1' disappeared while this job was running; it was not retried"}}
        """;

    private static (InferHubJobsClient Client, FakeHttpMessageHandler Handler) Create(HttpStatusCode status, string body, string mediaType = "application/json")
    {
        var handler = new FakeHttpMessageHandler(status, body, mediaType);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new InferHubJobsClient(http), handler);
    }

    private static ChatRequest Chat() => new()
    {
        Model = "qwen2.5:0.5b",
        Messages = [new ChatMessage { Role = "user", Content = "hi" }]
    };

    [Fact]
    public async Task SubmitWrapsTheChatRequestAndReturnsTheStatusAtOnce()
    {
        var (client, handler) = Create(HttpStatusCode.Accepted, RecordedQueued);

        var job = await client.SubmitAsync(Chat());

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/api/jobs", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("\"kind\":\"chat\"", handler.RequestBodies[0]);
        Assert.Contains("\"request\":{", handler.RequestBodies[0]);
        Assert.Contains("\"model\":\"qwen2.5:0.5b\"", handler.RequestBodies[0]);
        Assert.Contains("\"content\":\"hi\"", handler.RequestBodies[0]);
        Assert.Equal(Id, job.Id);
        Assert.Equal(JobStatuses.Queued, job.Status);
        Assert.Equal(JobStages.Waiting, job.Stage);
        Assert.False(job.IsTerminal);
        Assert.Null(job.Node);
    }

    [Fact]
    public async Task SubmittingAGenerateNamesItsKind()
    {
        var (client, handler) = Create(HttpStatusCode.Accepted, RecordedQueued);

        await client.SubmitAsync(new GenerateRequest { Model = "qwen2.5:0.5b", Prompt = "hi" });

        Assert.Contains("\"kind\":\"generate\"", handler.RequestBodies[0]);
        Assert.Contains("\"prompt\":\"hi\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task ARunningStatusCarriesTheNodesOwnReport()
    {
        var (client, _) = Create(HttpStatusCode.OK, RecordedPreparing);

        var job = await client.GetAsync(Id);

        Assert.Equal(JobStatuses.Running, job!.Status);
        Assert.Equal(JobStages.Preparing, job.Stage);
        Assert.Equal("live150-node", job.Node!.Name);
        Assert.Equal(DateTimeOffset.Parse("2026-11-16T11:40:18.3304721+00:00"), job.Node.ReportedAt);
        Assert.Equal(1, job.Attempts);
    }

    [Fact]
    public async Task AFinishedStatusHasExactTokens()
    {
        var (client, _) = Create(HttpStatusCode.OK, RecordedSucceeded);

        var job = await client.GetAsync(Id);

        Assert.True(job!.IsTerminal);
        Assert.Equal(300, job.Tokens);
        Assert.Equal(300, job.Pieces);
        Assert.Equal(340.23, job.TokensPerSecond);
    }

    [Fact]
    public async Task ASplitModelsStatusSaysHowFarEachMemberIs()
    {
        var (client, _) = Create(HttpStatusCode.OK, DerivedWithGroup);

        var job = await client.GetAsync(Id);

        Assert.StartsWith("group starting", job!.Detail);
        Assert.Equal("starting", job.Group!.State);
        Assert.False(job.Group.Ready);
        Assert.Equal(["shard-a", "shard-b", "shard-c"], job.Group.Members.Select(m => m.Node).ToArray());
        Assert.Null(job.Group.Members[0].ExpectMiB);
        Assert.Equal(9210, job.Group.Members[1].SentMiB);
        Assert.Equal(18420d / 25022d, job.Group.LoadedFraction!.Value, 6);

        // Found against a real group: once it runs, the count says nothing about a load.
        job.Group.State = "running";
        Assert.Null(job.Group.LoadedFraction);
    }

    [Fact]
    public async Task AnUnknownJobIsNullNotAnException()
    {
        var (client, _) = Create(HttpStatusCode.NotFound, """{"error":"job 'x' not found"}""");

        Assert.Null(await client.GetAsync(Id));
    }

    [Fact]
    public async Task TheResultOfARunningJobIsTheTextSoFarAndNotDone()
    {
        var (client, _) = Create(HttpStatusCode.Accepted, RecordedPartialResult);

        var result = await client.GetResultAsync(Id);

        Assert.False(result.Done);
        Assert.Equal(string.Empty, result.Text);
        Assert.NotNull(result.Chat);
    }

    [Fact]
    public async Task TheResultOfAFinishedJobIsTheChatAnswerWithWhoServedIt()
    {
        var (client, handler) = Create(HttpStatusCode.OK, RecordedResult);
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node";

        var result = await client.GetResultAsync(Id);

        Assert.Equal($"/api/jobs/{Id}/result", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.True(result.Done);
        Assert.Equal("Ah, the lighthouse keeper.", result.Text);
        Assert.Equal(300, result.Chat!.EvalCount);
        Assert.Equal("node", result.ServedBy);
        Assert.Null(result.Generate);
    }

    [Fact]
    public async Task TheResultOfAFailedJobThrowsItsOwnStatus()
    {
        var (client, _) = Create(HttpStatusCode.BadGateway, """{"error":"node 'gpu-1' disappeared while this job was running; it was not retried"}""");

        var thrown = await Assert.ThrowsAsync<InferHubException>(() => client.GetResultAsync(Id));

        Assert.Equal(HttpStatusCode.BadGateway, thrown.StatusCode);
        Assert.Contains("disappeared", thrown.Message);
    }

    [Fact]
    public async Task AFailedStatusIsReadNotThrown()
    {
        var (client, _) = Create(HttpStatusCode.OK, DerivedFailed);

        var job = await client.GetAsync(Id);

        Assert.Equal(JobStatuses.Failed, job!.Status);
        Assert.Equal("node_lost", job.Error!.Code);
        Assert.Equal(502, job.Error.Status);
    }

    [Fact]
    public async Task CancelReturnsTheStatusAndDroppingAFinishedJobReturnsNull()
    {
        var (client, handler) = Create(HttpStatusCode.Accepted, DerivedFailed.Replace("\"failed\"", "\"cancelled\""));

        var cancelled = await client.CancelAsync(Id);

        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.Equal(JobStatuses.Cancelled, cancelled!.Status);

        var (dropping, _) = Create(HttpStatusCode.NoContent, string.Empty);
        Assert.Null(await dropping.CancelAsync(Id));
    }

    [Fact]
    public async Task ListReadsTheEnvelope()
    {
        var (client, _) = Create(HttpStatusCode.OK, $$"""{"jobs":[{{RecordedSucceeded}},{{RecordedPreparing}}]}""");

        var jobs = await client.ListAsync();

        Assert.Equal([JobStatuses.Succeeded, JobStatuses.Running], jobs.Select(j => j.Status).ToArray());
    }

    [Fact]
    public async Task WatchYieldsEachStatusAndStopsAtTheTerminalOne()
    {
        var stream =
            $"event: status\ndata: {RecordedPreparing.Trim()}\n\n: waiting\n\n" +
            $"event: status\ndata: {RecordedSucceeded.Trim()}\n\nevent: status\ndata: {RecordedQueued.Trim()}\n\n";
        var (client, handler) = Create(HttpStatusCode.OK, stream, "text/event-stream");

        var seen = new List<string>();

        await foreach (var job in client.WatchAsync(Id))
        {
            seen.Add(job.Status);
        }

        Assert.Equal([JobStatuses.Running, JobStatuses.Succeeded], seen);
        Assert.Equal($"/api/jobs/{Id}/events", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task WaitPollsUntilTheJobIsFinished()
    {
        var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, RecordedQueued);
        handler.Enqueue(HttpStatusCode.OK, RecordedPreparing);
        handler.Enqueue(HttpStatusCode.OK, RecordedSucceeded);
        var client = new InferHubJobsClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") });

        var job = await client.WaitAsync(Id, new JobWaitOptions { PollInterval = TimeSpan.Zero });

        Assert.Equal(JobStatuses.Succeeded, job.Status);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task WaitingForAJobThatIsGoneThrowsA404AndACancelledWaitLeavesTheJobAlone()
    {
        var (gone, _) = Create(HttpStatusCode.NotFound, """{"error":"job not found"}""");
        var thrown = await Assert.ThrowsAsync<InferHubException>(() => gone.WaitAsync(Id, new JobWaitOptions { PollInterval = TimeSpan.Zero }));
        Assert.Equal(HttpStatusCode.NotFound, thrown.StatusCode);

        var (running, handler) = Create(HttpStatusCode.OK, RecordedPreparing);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(Id, new JobWaitOptions { PollInterval = TimeSpan.FromMilliseconds(10) }, stop.Token));
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public void TheJobsClientIsRegisteredWithTheOthers()
    {
        var services = new ServiceCollection();
        services.AddInferHubClient(options => options.BaseAddress = new Uri("http://localhost:5080/"));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<InferHubJobsClient>(provider.GetRequiredService<IInferHubJobsClient>());
    }
}
