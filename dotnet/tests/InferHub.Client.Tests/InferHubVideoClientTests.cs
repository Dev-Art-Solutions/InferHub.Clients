using System.Net;
using System.Text;
using InferHub.Client.Exceptions;
using InferHub.Client.Http;
using InferHub.Client.Models.Videos;

namespace InferHub.Client.Tests;

/// <summary>
/// The video surface (<c>/v1/videos</c> and <c>GET /api/videos/jobs</c>), phase 11.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal in this file was recorded from a live hub</b> — InferHub 3.37.0 on
/// <c>:5080</c>, 2026-09-02, one node serving <c>chat</c> and <c>embed</c> with
/// <c>tools.enabled=false</c> — by driving the routes with curl and pasting what came back,
/// unicode escapes and all. So was the empty job listing, which is the only success body that was
/// ever observed.
/// </para>
/// <para>
/// <b>The success shapes are derived, not recorded, and that is the honest state:</b> no node on
/// that hub provides <c>video</c>, so <b>no clip was ever rendered</b>. The <c>video</c> object at
/// each status, its <c>expires_at</c>, the content headers and a video row in the job listing are
/// taken from the hub's own serializers — <c>VideoRenderer.Object</c>,
/// <c>VideoRenderer.Progress</c>, <c>VideoRenderer.ExpiresAt</c> and <c>ImageJobView.Describe</c> —
/// which decide those bytes, rather than from what a client author expects them to look like. Each
/// is marked below. Phase 25 is where a real one arrives.
/// </para>
/// </remarks>
public class InferHubVideoClientTests
{
    // ---- recorded from the live hub -------------------------------------------------------

    private const string RecordedEmptyJobList = """
        {"jobs":[],"queued":0,"active":0,"retainedBytes":0,"retentionSeconds":300,"persistence":"none"}
        """;

    // The two routes of OpenAI's Videos API this hub refuses. Neither is a method on
    // IInferHubVideoClient — a method that could only throw reads as "not implemented yet" — and
    // both are recorded here because the sentence IS the documentation a caller needs.
    private const string RecordedListingNotSupported = """
        {"error":{"message":"listing videos is not supported: a video id is itself the capability to fetch the bytes, so this API does not hand a caller a way to enumerate other jobs. The console reads GET /api/videos/jobs with an operator's own client key. Keep the id POST /v1/videos returned.","type":"invalid_request_error","param":null,"code":"not_supported"}}
        """;

    private const string RecordedRemixNotSupported = """
        {"error":{"message":"remixing 'video_11111111222233334444555555555555' is not supported: nothing durable holds the request that made a video — no prompt, no negative prompt, by design (rule 7) — so there is nothing here to remix from. Send a new request with the prompt you want.","type":"invalid_request_error","param":null,"code":"not_supported"}}
        """;

    // The fleet holds the model and nothing is rendering video with it. Carries Retry-After: 30,
    // and it is NOT the 404 below — one is worth retrying later and the other never is.
    private const string RecordedNoVideoNode = """
        {"error":{"message":"no node currently provides 'video' for model 'llava:latest'","type":"api_error","param":null,"code":"capability_unavailable"}}
        """;

    private const string RecordedModelNotFound = """
        {"error":{"message":"model 'wan2.2' not found","type":"not_found_error","param":"model","code":"model_not_found"}}
        """;

    // One 404 for three conditions: no such clip, not yours, and an IMAGE job's id. A malformed id
    // earns it too — "that is not a valid id" would tell a caller their guess was well-formed
    // enough to be checked.
    private const string RecordedVideoNotFound = """
        {"error":{"message":"video 'video_11111111222233334444555555555555' not found","type":"not_found_error","param":"id","code":"video_not_found"}}
        """;

    // The phase's best trap, recorded both ways round: 1920x1080 is a perfectly good IMAGE size.
    private const string RecordedSizeNotMultipleOfSixteen = """
        {"error":{"message":"size '1920x1080' must have both sides a multiple of 16 — a video pipeline downsamples by 16 where an image pipeline downsamples by 8, and this is one of the two grids that differ","type":"invalid_request_error","param":"size","code":null}}
        """;

    private const string RecordedSecondsTooLong = """
        {"error":{"message":"seconds must be at most 60 — and the model you named will have a shorter list of its own","type":"invalid_request_error","param":"seconds","code":null}}
        """;

    // The header refusals name the HEADER in `param`, not a body field — surfaced verbatim. The
    // second is the invariant-culture bug as the hub sees it.
    private const string RecordedBadSteps = """
        {"error":{"message":"X-InferHub-Video-Steps: 'many' is not an integer between 1 and 150","type":"invalid_request_error","param":"X-InferHub-Video-Steps","code":null}}
        """;

    private const string RecordedBadGuidance = """
        {"error":{"message":"X-InferHub-Video-Guidance: '5,5' is not a number between 0 and 50 (use a decimal point)","type":"invalid_request_error","param":"X-InferHub-Video-Guidance","code":null}}
        """;

    // ---- derived from the hub's own serializers, never observed ----------------------------

    // VideoRenderer.Object: a dictionary written with WhenWritingNull, so which keys are PRESENT is
    // the contract. A queued clip has no size (the caller named none and the recipe's default is
    // the node's business), no completed_at and no expires_at — and progress 0, which is true.
    private const string DerivedQueued = """
        {"id":"video_9d1f4c2ab8f04d2e9a7c1e5b3d6f8a02","object":"video","model":"wan2.2","status":"queued","progress":0,"created_at":1788000000}
        """;

    // Mid-render, with the size the caller asked for echoed back. VideoRenderer.Progress caps this
    // at 99 until the job is terminal: 46 is step 13 of 28.
    private const string DerivedRunning = """
        {"id":"video_9d1f4c2ab8f04d2e9a7c1e5b3d6f8a02","object":"video","model":"wan2.2","status":"in_progress","progress":46,"created_at":1788000000,"size":"832x480","seconds":5}
        """;

    // The last frame before the end: 99, not 100. A client that stopped at 100 would have stopped
    // one round trip before the bytes exist.
    private const string DerivedAlmostDone = """
        {"id":"video_9d1f4c2ab8f04d2e9a7c1e5b3d6f8a02","object":"video","model":"wan2.2","status":"in_progress","progress":99,"created_at":1788000000,"size":"832x480","seconds":5}
        """;

    // expires_at IS completed_at plus the store's RetentionSeconds (VideoRenderer.ExpiresAt), so the
    // field cannot drift from the behaviour: 1788000480 = 1788000180 + 300.
    private const string DerivedCompleted = """
        {"id":"video_9d1f4c2ab8f04d2e9a7c1e5b3d6f8a02","object":"video","model":"wan2.2","status":"completed","progress":100,"created_at":1788000000,"completed_at":1788000180,"expires_at":1788000480,"size":"832x480","seconds":5.04}
        """;

    // A failure arrives on a 200 with status: failed — polling is how this dialect reports one.
    private const string DerivedFailed = """
        {"id":"video_9d1f4c2ab8f04d2e9a7c1e5b3d6f8a02","object":"video","model":"wan2.2","status":"failed","progress":0,"created_at":1788000000,"completed_at":1788000090,"error":{"code":"worker_error","message":"the video worker returned no video"}}
        """;

    private const string DerivedDeleted = """
        {"id":"video_9d1f4c2ab8f04d2e9a7c1e5b3d6f8a02","object":"video","deleted":true}
        """;

    // ImageJobView.Describe for a VIDEO job: the same document the images listing serves, with
    // capability telling them apart and images[0].url pointing at this capability's own content
    // route rather than the images one (hub 59 D6).
    private const string DerivedVideoJobRow = """
        {"jobs":[{"id":"9d1f4c2a-b8f0-4d2e-9a7c-1e5b3d6f8a02","state":"succeeded","model":"wan2.2","capability":"video","n":1,"createdAt":"2026-09-02T08:00:00+00:00","completedAt":"2026-09-02T08:03:00+00:00","node":"node-1","step":28,"totalSteps":28,"images":[{"index":0,"url":"/v1/videos/video_9d1f4c2ab8f04d2e9a7c1e5b3d6f8a02/content","size":"832x480","seed":42,"bytes":4194304,"seconds":5.04,"projection":"flat"}],"megapixelSteps":33.6}],"queued":0,"active":1,"retainedBytes":4194304,"retentionSeconds":300,"persistence":"none"}
        """;

    private const string VideoId = "video_9d1f4c2ab8f04d2e9a7c1e5b3d6f8a02";

    // -----------------------------------------------------------------------------------------

    private static (InferHubVideoClient Client, FakeHttpMessageHandler Handler) CreateClient(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json")
    {
        var handler = new FakeHttpMessageHandler(status, body, mediaType);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new InferHubVideoClient(http), handler);
    }

    private static InferHubVideoClient Scripted(SequenceHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") });

    private static VideoGenerationRequest Request(string model = "wan2.2")
        => VideoGenerationRequest.Create(model, "a kite over a grey sea");

    // ---- creating ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_posts_the_dialects_own_path_with_the_prompt_in_the_body()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedQueued);

        await client.CreateAsync(Request());

        Assert.EndsWith("v1/videos", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("\"prompt\":\"a kite over a grey sea\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task CreateAsync_reads_the_queued_object_and_served_by()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedQueued);
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node-1";

        var clip = await client.CreateAsync(Request());

        Assert.Equal(VideoId, clip.Id);
        Assert.Equal("video", clip.Object);
        Assert.Equal(VideoStatuses.Queued, clip.Status);
        Assert.Equal(0, clip.Progress);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788000000), clip.Created);
        Assert.False(clip.IsTerminal);
        Assert.Equal("node-1", clip.ServedBy);

        // Absence is a fact: before the worker answers, the hub does not know the geometry unless
        // the caller named it, and a zero here would be the hub declaring a model's native size.
        Assert.Null(clip.Size);
        Assert.Null(clip.Seconds);
        Assert.Null(clip.Completed);
        Assert.Null(clip.Expires);
    }

    /// <summary>
    /// <c>seconds</c> and <c>size</c> are OpenAI's own body fields; the InferHub knobs are headers.
    /// <c>negative_prompt</c> stays in the body on purpose — it is the caller's own words, and a
    /// header is the one part of a request every proxy in the path writes down.
    /// </summary>
    [Fact]
    public async Task The_extension_knobs_travel_as_headers_and_the_caller_s_words_never_do()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedQueued);

        var request = Request();
        request.Size = VideoSizes.Wide480;
        request.Seconds = 5;
        request.NegativePrompt = "blurry";
        request.Options = new VideoOptions { Steps = 30, Guidance = 5.5, Seed = 42 };

        await client.CreateAsync(request);

        var sent = handler.Requests[0];
        Assert.Equal("30", sent.Headers.GetValues(VideoHeaders.Steps).Single());
        Assert.Equal("5.5", sent.Headers.GetValues(VideoHeaders.Guidance).Single());
        Assert.Equal("42", sent.Headers.GetValues(VideoHeaders.Seed).Single());

        Assert.Contains("\"size\":\"832x480\"", handler.RequestBodies[0]);
        Assert.Contains("\"seconds\":5", handler.RequestBodies[0]);
        Assert.Contains("\"negative_prompt\":\"blurry\"", handler.RequestBodies[0]);
        Assert.DoesNotContain("steps", handler.RequestBodies[0]);
    }

    /// <summary>
    /// The bug that only reproduces on somebody else's laptop, and the hub's own sentence about it:
    /// <c>X-InferHub-Video-Guidance: '5,5' is not a number between 0 and 50 (use a decimal point)</c>.
    /// </summary>
    [Fact]
    public async Task Every_number_in_a_header_is_written_with_an_invariant_decimal_point()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("bg-BG");

        try
        {
            var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedQueued);

            var request = Request();
            request.Options = new VideoOptions { Guidance = 5.5 };

            await client.CreateAsync(request);

            Assert.Equal("5.5", handler.Requests[0].Headers.GetValues(VideoHeaders.Guidance).Single());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task A_model_the_fleet_holds_with_nobody_rendering_video_is_a_503_with_a_retry_after()
    {
        var (client, handler) = CreateClient(HttpStatusCode.ServiceUnavailable, RecordedNoVideoNode);
        handler.ResponseHeaders["Retry-After"] = "30";

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.CreateAsync(Request("llava:latest")));

        Assert.Equal(VideoErrorCodes.CapabilityUnavailable, error.ErrorCode);
        Assert.Equal("api_error", error.ErrorType);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
        Assert.Contains("no node currently provides 'video'", error.Message);
    }

    /// <summary>
    /// The 404 beside it. The distinction is the whole point: one is worth retrying later and the
    /// other never is, and both would be a bare "it did not work" without the code.
    /// </summary>
    [Fact]
    public async Task A_model_the_fleet_does_not_have_is_a_404_and_carries_no_retry_after()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedModelNotFound);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.CreateAsync(Request()));

        Assert.Equal("model_not_found", error.ErrorCode);
        Assert.Equal("model", error.Param);
        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public async Task The_size_and_seconds_refusals_name_the_body_field_and_the_header_ones_name_the_header()
    {
        var (size, _) = CreateClient(HttpStatusCode.BadRequest, RecordedSizeNotMultipleOfSixteen);
        var sizeError = await Assert.ThrowsAsync<InferHubOpenAiException>(() => size.CreateAsync(Request()));
        Assert.Equal("size", sizeError.Param);
        Assert.Contains("multiple of 16", sizeError.Message);

        var (seconds, _) = CreateClient(HttpStatusCode.BadRequest, RecordedSecondsTooLong);
        var secondsError = await Assert.ThrowsAsync<InferHubOpenAiException>(() => seconds.CreateAsync(Request()));
        Assert.Equal("seconds", secondsError.Param);

        var (steps, _) = CreateClient(HttpStatusCode.BadRequest, RecordedBadSteps);
        var stepsError = await Assert.ThrowsAsync<InferHubOpenAiException>(() => steps.CreateAsync(Request()));
        Assert.Equal(VideoHeaders.Steps, stepsError.Param);

        var (guidance, _) = CreateClient(HttpStatusCode.BadRequest, RecordedBadGuidance);
        var guidanceError = await Assert.ThrowsAsync<InferHubOpenAiException>(() => guidance.CreateAsync(Request()));
        Assert.Equal(VideoHeaders.Guidance, guidanceError.Param);
    }

    [Fact]
    public async Task An_empty_model_or_prompt_is_refused_here_rather_than_over_the_network()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedQueued);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateAsync(new VideoGenerationRequest { Model = " ", Prompt = "a kite" }));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateAsync(new VideoGenerationRequest { Model = "wan2.2", Prompt = "" }));

        Assert.Empty(handler.Requests);
    }

    // ---- reading ----------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_returns_null_for_the_one_404_that_covers_three_conditions()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedVideoNotFound);

        Assert.Null(await client.GetAsync(VideoId));
    }

    /// <summary>
    /// A failed render is emphatically not a null: it is a document that says what went wrong, and
    /// it arrives on a <c>200</c>.
    /// </summary>
    [Fact]
    public async Task A_failed_clip_is_a_document_with_a_code_not_an_exception()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, DerivedFailed);

        var clip = await client.GetAsync(VideoId);

        Assert.NotNull(clip);
        Assert.Equal(VideoStatuses.Failed, clip!.Status);
        Assert.True(clip.IsTerminal);
        Assert.Equal("worker_error", clip.Error?.Code);
        Assert.Equal("the video worker returned no video", clip.Error?.Message);
    }

    [Fact]
    public async Task A_completed_clip_reports_when_its_bytes_stop_being_fetchable()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, DerivedCompleted);

        var clip = await client.GetAsync(VideoId);

        Assert.Equal(VideoStatuses.Completed, clip!.Status);
        Assert.Equal(100, clip.Progress);
        Assert.Equal(5.04, clip.Seconds);
        Assert.Equal("832x480", clip.Size);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788000480), clip.Expires);

        // The retention window the store enforces, applied to the completion — not a promise made
        // separately from the behaviour.
        Assert.Equal(TimeSpan.FromSeconds(300), clip.Expires - clip.Completed);
    }

    // ---- watching ---------------------------------------------------------------------------

    /// <summary>
    /// There is no SSE here — the images events route is scoped to the image capabilities and a
    /// video id on it is a 404 — so the watch polls, and it ends on the terminal document rather
    /// than on <c>progress == 100</c>, which the hub only ever writes once the render is over.
    /// </summary>
    [Fact]
    public async Task WatchAsync_polls_until_the_clip_is_terminal_and_yields_only_what_changed()
    {
        var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, DerivedQueued);
        handler.Enqueue(HttpStatusCode.OK, DerivedQueued);      // nothing new — not yielded
        handler.Enqueue(HttpStatusCode.OK, DerivedRunning);
        handler.Enqueue(HttpStatusCode.OK, DerivedAlmostDone);
        handler.Enqueue(HttpStatusCode.OK, DerivedCompleted);

        var seen = new List<Video>();

        await foreach (var frame in Scripted(handler).WatchAsync(
            VideoId,
            new VideoWatchOptions { PollInterval = TimeSpan.FromMilliseconds(1) }))
        {
            seen.Add(frame);
        }

        Assert.Equal(
            [VideoStatuses.Queued, VideoStatuses.InProgress, VideoStatuses.InProgress, VideoStatuses.Completed],
            seen.Select(frame => frame.Status));

        Assert.Equal([0, 46, 99, 100], seen.Select(frame => frame.Progress));
        Assert.Equal(5, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Contains("v1/videos/", request.RequestUri!.ToString()));
    }

    [Fact]
    public async Task WatchAsync_yields_every_poll_when_asked_to()
    {
        var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, DerivedQueued);
        handler.Enqueue(HttpStatusCode.OK, DerivedQueued);
        handler.Enqueue(HttpStatusCode.OK, DerivedFailed);

        var seen = 0;

        await foreach (var _ in Scripted(handler).WatchAsync(
            VideoId,
            new VideoWatchOptions { PollInterval = TimeSpan.FromMilliseconds(1), YieldUnchanged = true }))
        {
            seen++;
        }

        Assert.Equal(3, seen);
    }

    [Fact]
    public async Task WatchAsync_on_a_clip_that_is_gone_says_so_rather_than_looping_forever()
    {
        var handler = new SequenceHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, RecordedVideoNotFound);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(async () =>
        {
            await foreach (var _ in Scripted(handler).WatchAsync(VideoId))
            {
                // no frame is expected
            }
        });

        Assert.Equal(VideoErrorCodes.NotFound, error.ErrorCode);
    }

    // ---- the bytes --------------------------------------------------------------------------

    [Fact]
    public async Task OpenContentAsync_fetches_one_clip_from_a_route_with_no_index()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, "fake-mp4-bytes", "video/mp4");
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node-1";

        await using var content = await client.OpenContentAsync(VideoId);

        Assert.EndsWith($"v1/videos/{VideoId}/content", handler.Requests[0].RequestUri!.ToString());
        Assert.StartsWith("video/mp4", content.ContentType);
        Assert.Equal("node-1", content.ServedBy);
        Assert.Equal("fake-mp4-bytes", Encoding.UTF8.GetString(await content.ReadAllBytesAsync()));
    }

    /// <summary>
    /// The one request in this client that must never be re-sent: the read unlinks the bytes at the
    /// hub, so a retry after a dropped connection collects a 410 and the clip is gone for good.
    /// </summary>
    [Fact]
    public async Task The_content_request_is_marked_never_retry()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, "fake-mp4-bytes", "video/mp4");

        await using var content = await client.OpenContentAsync(VideoId);

        Assert.True(InferHubRequestOptions.IsNeverRetry(handler.Requests[0]));
    }

    /// <summary>
    /// Three refusals, three different fixes. Flattening any of them into a null would tell a caller
    /// "there was never a clip", which is the one thing that is false about the 410.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Conflict, VideoErrorCodes.NotReady)]
    [InlineData(HttpStatusCode.Gone, VideoErrorCodes.Expired)]
    [InlineData(HttpStatusCode.NotFound, VideoErrorCodes.NotFound)]
    public async Task Content_that_is_not_there_surfaces_the_code_that_says_why(HttpStatusCode status, string code)
    {
        // Derived: VideoRenderer.Unavailable writes all three, and none was reachable without a
        // render. The sentences are its own, abbreviated to the part a caller keys on.
        var body = """
            {"error":{"message":"video 'VIDEO_ID' is not available","type":"invalid_request_error","param":null,"code":"CODE"}}
            """
            .Replace("VIDEO_ID", VideoId)
            .Replace("CODE", code);

        var (client, _) = CreateClient(status, body);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.OpenContentAsync(VideoId));

        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_cancels_and_drops_and_answers_in_the_dialects_shape()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedDeleted);

        var deleted = await client.DeleteAsync(VideoId);

        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.EndsWith($"v1/videos/{VideoId}", handler.Requests[0].RequestUri!.ToString());
        Assert.True(deleted.Deleted);
        Assert.Equal(VideoId, deleted.Id);
    }

    // ---- the listing ------------------------------------------------------------------------

    [Fact]
    public async Task ListJobsAsync_reads_the_hub_route_the_dialect_does_not_have()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedEmptyJobList);

        var listing = await client.ListJobsAsync();

        Assert.EndsWith("api/videos/jobs", handler.Requests[0].RequestUri!.ToString());
        Assert.Empty(listing.Jobs);
        Assert.Equal(300, listing.RetentionSeconds);
        Assert.Equal("none", listing.Persistence);
    }

    /// <summary>
    /// The same job document phase 10 models, with <c>capability</c> telling the modalities apart
    /// and the row's own <c>url</c> pointing at this capability's content route — a video row that
    /// pointed at the images one would be a 404 with a plausible shape.
    /// </summary>
    [Fact]
    public async Task A_video_row_carries_its_capability_its_duration_and_its_own_content_route()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, DerivedVideoJobRow);

        var job = Assert.Single((await client.ListJobsAsync()).Jobs);

        Assert.Equal("video", job.Capability);
        Assert.Equal("succeeded", job.State);
        var output = Assert.Single(job.Images!);
        Assert.Equal($"/v1/videos/{VideoId}/content", output.Url);
        Assert.Equal(5.04, output.Seconds);

        // The id spellings differ by route, and this is where a caller crosses between them.
        Assert.Equal(VideoId, VideoIdentifier.ToVideoId(job.Id));
    }

    [Fact]
    public void The_two_id_spellings_convert_both_ways_and_tolerate_being_handed_the_wrong_one()
    {
        const string jobId = "9d1f4c2a-b8f0-4d2e-9a7c-1e5b3d6f8a02";

        Assert.Equal(VideoId, VideoIdentifier.ToVideoId(jobId));
        Assert.Equal(jobId, VideoIdentifier.ToJobId(VideoId));

        Assert.Equal(VideoId, VideoIdentifier.ToVideoId(VideoId));
        Assert.Equal(jobId, VideoIdentifier.ToJobId(jobId));
    }

    // ---- the two refusals this client offers no method for -----------------------------------

    /// <summary>
    /// <c>GET /v1/videos</c> and <c>POST /v1/videos/{id}/remix</c> are <c>501</c>s with a sentence,
    /// not gaps: an id is itself the capability to fetch the bytes, and nothing durable holds the
    /// prompt that made a clip. A caller who hand-rolls either request gets an exception carrying
    /// the code and the reason, which is what this asserts.
    /// </summary>
    [Fact]
    public async Task Listing_and_remix_arrive_as_not_supported_with_the_reason_intact()
    {
        foreach (var body in new[] { RecordedListingNotSupported, RecordedRemixNotSupported })
        {
            using var response = new HttpResponseMessage(HttpStatusCode.NotImplemented)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            var error = await Assert.ThrowsAsync<InferHubOpenAiException>(
                () => InferHubResponse.EnsureSuccessAsync(response, CancellationToken.None));

            Assert.Equal(VideoErrorCodes.NotSupported, error.ErrorCode);
            Assert.Equal(HttpStatusCode.NotImplemented, error.StatusCode);
        }

        Assert.Contains("a video id is itself the capability", RecordedListingNotSupported);
        Assert.Contains("nothing durable holds the request", RecordedRemixNotSupported);
    }

    // ---- the grid that is not the image grid -------------------------------------------------

    /// <summary>
    /// <c>1920x1080</c> is a fine image size and a <c>400</c> here, recorded above. The constants
    /// exist so the common case never sends it.
    /// </summary>
    [Theory]
    [InlineData("832x480", true)]
    [InlineData("1280x720", true)]
    [InlineData("1920x1088", true)]
    [InlineData("1920x1080", false)]
    [InlineData("1024x1000", false)]
    [InlineData("4096x2048", false)]
    [InlineData("32x32", false)]
    [InlineData("1024", false)]
    [InlineData("", false)]
    public void A_video_size_is_a_multiple_of_sixteen_where_an_image_size_is_a_multiple_of_eight(string size, bool valid)
    {
        Assert.Equal(valid, VideoSizes.IsValid(size));
    }

    [Fact]
    public void The_named_sizes_are_all_ones_the_hub_accepts()
    {
        Assert.All(
            new[] { VideoSizes.Wide480, VideoSizes.Square480, VideoSizes.Portrait480, VideoSizes.Wide720, VideoSizes.Wide1088 },
            size => Assert.True(VideoSizes.IsValid(size), size));
    }
}
