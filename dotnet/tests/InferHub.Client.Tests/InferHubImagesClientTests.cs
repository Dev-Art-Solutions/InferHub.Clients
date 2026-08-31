using System.Net;
using System.Text;
using InferHub.Client.Configuration;
using InferHub.Client.Exceptions;
using InferHub.Client.Models.Images;

namespace InferHub.Client.Tests;

/// <summary>
/// The image surface (<c>/v1/images/*</c> and <c>/api/images/jobs</c>), phase 10.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal in this file was recorded from a live hub</b> — InferHub 3.37.0, one node
/// serving <c>chat</c> and <c>embed</c> with <c>tools.enabled=false</c> — by driving both routes
/// with curl and pasting what came back, escapes and all. So was the empty job listing, which is
/// the only success body that was ever observed.
/// </para>
/// <para>
/// <b>The success shapes are derived, not recorded, and that is the honest state:</b> no node on
/// that hub provides <c>image</c> or <c>image-edit</c>, so <b>no image was ever generated</b>.
/// The synchronous envelope, the job document with its <c>images[]</c>, the SSE progress frames and
/// the content headers are taken from the hub's own serializers — <c>ImageRenderer.Envelope</c>,
/// <c>ImageJobView.Describe</c>, <c>ImageJobEndpoints.WriteEventAsync</c> and
/// <c>SeamRepairModes.HeadersFor</c> — which decide those bytes, rather than from what a client
/// author expects them to look like. Each is marked below. Phase 25 is where a real one arrives.
/// </para>
/// </remarks>
public class InferHubImagesClientTests
{
    // ---- recorded from the live hub -------------------------------------------------------

    private const string RecordedEmptyJobList = """
        {"jobs":[],"queued":0,"active":0,"retainedBytes":0,"retentionSeconds":300,"persistence":"none"}
        """;

    // The fleet holds the model and nothing is rendering with it. Carries Retry-After: 30, and it is
    // NOT the 404 below — one is worth retrying later and the other never is.
    private const string RecordedNoImageNode = """
        {"error":{"message":"no node currently provides 'image' for model 'llava:latest'","type":"api_error","param":null,"code":"capability_unavailable"}}
        """;

    private const string RecordedNoImageEditNode = """
        {"error":{"message":"no node currently provides 'image-edit' for model 'llava:latest'","type":"api_error","param":null,"code":"capability_unavailable"}}
        """;

    private const string RecordedModelNotFound = """
        {"error":{"message":"model 'sdxl' not found","type":"not_found_error","param":"model","code":"model_not_found"}}
        """;

    private const string RecordedJobNotFound = """
        {"error":{"message":"image job '11111111-2222-3333-4444-555555555555' not found","type":"not_found_error","param":"id","code":"job_not_found"}}
        """;

    // The refusal that explains itself: there is no URL because there is no store.
    private const string RecordedUrlFormatRefused = """
        {"error":{"message":"response_format=url is not supported: this coordinator never stores a generated image, so there is no URL to serve it from. Use response_format=b64_json.","type":"invalid_request_error","param":"response_format","code":null}}
        """;

    private const string RecordedSizeNotMultipleOfEight = """
        {"error":{"message":"size '1001x1000' must have both sides a multiple of 8 (every latent-diffusion pipeline downsamples by 8)","type":"invalid_request_error","param":"size","code":null}}
        """;

    private const string RecordedBatchTooLarge = """
        {"error":{"message":"n must be at most 4 (Images:MaxBatch)","type":"invalid_request_error","param":"n","code":null}}
        """;

    // The header refusals name the HEADER in `param`, not a body field — surfaced verbatim.
    private const string RecordedBadSteps = """
        {"error":{"message":"X-InferHub-Image-Steps: 'many' is not an integer between 1 and 150","type":"invalid_request_error","param":"X-InferHub-Image-Steps","code":null}}
        """;

    private const string RecordedBadStrength = """
        {"error":{"message":"X-InferHub-Image-Strength: '0,75' is not a number between 0 and 1 (use a decimal point)","type":"invalid_request_error","param":"X-InferHub-Image-Strength","code":null}}
        """;

    private const string RecordedBadSeamRepair = """
        {"error":{"message":"X-InferHub-Image-Seam-Repair: 'magic' is not a seam-repair mechanism. Use 'blend' (a wrapped feather across the join — milliseconds, no steps), 'diffuse' (an inpainting pass over the join — slower, billed as the steps it runs), or 'off'.","type":"invalid_request_error","param":"X-InferHub-Image-Seam-Repair","code":null}}
        """;

    // The two the C# type system makes unreachable — a variation has no prompt and no mask. Kept
    // because they are recorded bodies a phase-15 case is built from, and because they are the
    // argument for ImageVariationRequest being its own type.
    private const string RecordedVariationWithPrompt = """
        {"error":{"message":"a variation takes no prompt — it is 'more of this picture'. Use POST /v1/images/edits with no mask for image-to-image with a prompt.","type":"invalid_request_error","param":"prompt","code":null}}
        """;

    private const string RecordedVariationWithMask = """
        {"error":{"message":"a variation takes no mask. Use POST /v1/images/edits to change part of a picture.","type":"invalid_request_error","param":"mask","code":null}}
        """;

    private const string RecordedMultipartWithoutOperation = """
        {"error":{"message":"a multipart image job must name its operation: 'edit' or 'variation'. Send JSON to generate.","type":"invalid_request_error","param":"operation","code":null}}
        """;

    private const string RecordedEditWithoutPrompt = """
        {"error":{"message":"prompt is required: an edit says what to change.","type":"invalid_request_error","param":"prompt","code":null}}
        """;

    private const string RecordedBadMaskConvention = """
        {"error":{"message":"X-InferHub-Mask-Convention: 'inverted' is not a mask convention. Use 'openai' (the default — TRANSPARENT pixels are the area to edit, as OpenAI's API defines it) or 'luminance' (WHITE pixels are the area to edit, as diffusers defines it).","type":"invalid_request_error","param":"X-InferHub-Mask-Convention","code":null}}
        """;

    // ---- derived from the hub's own serializers, never observed ----------------------------

    // ImageRenderer.Envelope: a dictionary, so which keys are PRESENT is the contract. `seam_delta`
    // only where there is a seam, `revised_prompt` always and always null, `prompt_augmented` and
    // `trigger` only for a recipe that has a trigger.
    private const string DerivedSyncEnvelope = """
        {"created":1788000000,"data":[{"b64_json":"aW1hZ2UtYnl0ZXM=","size":"1024x1024","seed":42,"projection":"flat","revised_prompt":null}],"prompt_augmented":true,"trigger":"qwen-360 style"}
        """;

    // The same envelope for an equirectangular render that asked for a repair: seam_delta is the
    // CURRENT image's, seam_delta_before is what it said before. Equal numbers would be a repair
    // that did not help and was discarded.
    private const string DerivedPanoramaEnvelope = """
        {"created":1788000001,"data":[{"b64_json":"cGFub3JhbWE=","size":"2048x1024","seed":7,"projection":"equirectangular","seam_delta":0.014,"seam_repair":"blend","seam_delta_before":0.19,"revised_prompt":null}],"warnings":["the recipe clamped steps to 40"]}
        """;

    // ImageJobView.Describe, queued: `images` absent because there is nothing to fetch, so "is it
    // ready" is answerable from the shape rather than from the state name plus a rule.
    private const string DerivedQueuedJob = """
        {"id":"7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60","state":"queued","model":"sdxl","capability":"image","n":1,"createdAt":"2026-08-31T19:36:49.1234567+00:00","queuePosition":3}
        """;

    private const string DerivedRunningJob = """
        {"id":"7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60","state":"running","model":"sdxl","capability":"image","n":1,"createdAt":"2026-08-31T19:36:49.1234567+00:00","startedAt":"2026-08-31T19:36:52.7654321+00:00","node":"65075bfb-5968-48d6-8e54-9fc20814b73b","step":7,"totalSteps":28}
        """;

    private const string DerivedSucceededJob = """
        {"id":"7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60","state":"succeeded","model":"sdxl","capability":"image","n":1,"createdAt":"2026-08-31T19:36:49.1234567+00:00","startedAt":"2026-08-31T19:36:52.7654321+00:00","completedAt":"2026-08-31T19:37:31.0000000+00:00","node":"65075bfb-5968-48d6-8e54-9fc20814b73b","step":28,"totalSteps":28,"images":[{"index":0,"url":"/api/images/jobs/7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60/content/0","size":"1024x1024","seed":42,"bytes":1481232,"projection":"flat"}],"megapixelSteps":29.36}
        """;

    private const string DerivedFailedJob = """
        {"id":"7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60","state":"failed","model":"sdxl","capability":"image","n":1,"createdAt":"2026-08-31T19:36:49.1234567+00:00","reason":"failed","error":"this recipe supports 1024x1024, 1152x896 and 896x1152","errorCode":"invalid_request"}
        """;

    // A video job, rendered by the SAME serializer — which is the whole reason the type is called
    // MediaJob. Note where its content lives: OpenAI's own video route, not the images one.
    private const string DerivedVideoJob = """
        {"id":"9c2e1a77-8b4d-4e11-a0f3-6d5c4b3a2190","state":"succeeded","model":"ltx-video","capability":"video","n":1,"createdAt":"2026-08-31T19:40:00.0000000+00:00","images":[{"index":0,"url":"/v1/videos/vid_9c2e1a77/content","size":"1280x720","bytes":41118002,"seconds":5.0,"projection":"flat"}]}
        """;

    // ImageJobEndpoints.WriteEventAsync: `event: <state>` then `data: <the whole job document>`.
    // The stream ends after the terminal frame; there is no [DONE] sentinel.
    private static readonly string DerivedJobEventStream =
        Frame("queued", DerivedQueuedJob)
        + Frame("running", DerivedRunningJob)
        + Frame("succeeded", DerivedSucceededJob);

    private static string Frame(string state, string json) => $"event: {state}\ndata: {json}\n\n";

    // -----------------------------------------------------------------------------------------

    private static (InferHubImagesClient Client, FakeHttpMessageHandler Handler) CreateClient(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json")
    {
        var handler = new FakeHttpMessageHandler(status, body, mediaType);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new InferHubImagesClient(http), handler);
    }

    private static ImageGenerationRequest Generation(string model = "sdxl")
        => ImageGenerationRequest.Create(model, "a lighthouse in fog");

    private static Stream Picture() => new MemoryStream(Encoding.UTF8.GetBytes("PNG\r\n\nfake"));

    // ---- the synchronous routes ------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_posts_the_openai_path_with_the_prompt_in_the_body()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);

        await client.GenerateAsync(Generation());

        Assert.EndsWith("v1/images/generations", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("\"prompt\":\"a lighthouse in fog\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task GenerateAsync_reads_the_envelope_the_extras_and_served_by()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node-1";

        var answer = await client.GenerateAsync(Generation());

        Assert.Equal(1788000000, answer.Created);
        var image = Assert.Single(answer.Data);
        Assert.Equal("1024x1024", image.Size);
        Assert.Equal(42, image.Seed);
        Assert.Equal(ImageProjections.Flat, image.Projection);
        Assert.Equal("image-bytes", Encoding.UTF8.GetString(image.ToBytes()));

        // Always present and always null: nothing here revises a prompt, and the augmentation is
        // reported as the recipe's own trigger constant instead of by echoing the caller's words.
        Assert.Null(image.RevisedPrompt);
        Assert.True(answer.PromptAugmented);
        Assert.Equal("qwen-360 style", answer.Trigger);
        Assert.Equal("node-1", answer.ServedBy);
    }

    /// <summary>
    /// A flat render measures no seam, so the field is absent rather than zero — a permanent zero
    /// would read as "perfectly seamless" instead of "not applicable".
    /// </summary>
    [Fact]
    public async Task A_seam_delta_of_a_repaired_panorama_reports_both_numbers_and_a_flat_render_reports_none()
    {
        var (panorama, _) = CreateClient(HttpStatusCode.OK, DerivedPanoramaEnvelope);
        var repaired = (await panorama.GenerateAsync(Generation())).Data[0];

        Assert.Equal(ImageProjections.Equirectangular, repaired.Projection);
        Assert.Equal(SeamRepairModes.Blend, repaired.SeamRepair);
        Assert.Equal(0.014, repaired.SeamDelta);
        Assert.Equal(0.19, repaired.SeamDeltaBefore);

        var (flat, _) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);
        var plain = (await flat.GenerateAsync(Generation())).Data[0];

        Assert.Null(plain.SeamDelta);
        Assert.Null(plain.SeamRepair);
    }

    [Fact]
    public async Task Warnings_arrive_as_the_hub_wrote_them()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, DerivedPanoramaEnvelope);

        var answer = await client.GenerateAsync(Generation());

        Assert.Equal("the recipe clamped steps to 40", Assert.Single(answer.Warnings!));
    }

    [Fact]
    public async Task The_extension_knobs_travel_as_headers_and_never_in_the_body()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);

        var request = Generation();
        request.Options = new ImageOptions
        {
            Steps = 28,
            Guidance = 7.5,
            Seed = 42,
            SeamRepair = SeamRepairModes.Blend
        };

        await client.GenerateAsync(request);

        var sent = handler.Requests[0];
        Assert.Equal("28", sent.Headers.GetValues(ImageHeaders.Steps).Single());
        Assert.Equal("7.5", sent.Headers.GetValues(ImageHeaders.Guidance).Single());
        Assert.Equal("42", sent.Headers.GetValues(ImageHeaders.Seed).Single());
        Assert.Equal("blend", sent.Headers.GetValues(ImageHeaders.SeamRepair).Single());

        // A body field would collide with whatever OpenAI adds to this API next.
        Assert.DoesNotContain("steps", handler.RequestBodies[0]);
        Assert.DoesNotContain("guidance", handler.RequestBodies[0]);
    }

    /// <summary>
    /// The bug that only reproduces on somebody else's laptop: a German or Bulgarian default culture
    /// formats 7.5 as "7,5", which the hub refuses as "not a number (use a decimal point)".
    /// </summary>
    [Fact]
    public async Task Every_number_in_a_header_is_written_with_an_invariant_decimal_point()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("bg-BG");

        try
        {
            var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);

            var request = Generation();
            request.Options = new ImageOptions { Guidance = 7.5, Strength = 0.75 };

            await client.GenerateAsync(request);

            Assert.Equal("7.5", handler.Requests[0].Headers.GetValues(ImageHeaders.Guidance).Single());
            Assert.Equal("0.75", handler.Requests[0].Headers.GetValues(ImageHeaders.Strength).Single());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task An_edit_writes_every_field_before_the_file_parts()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);

        await client.EditAsync(new ImageEditRequest
        {
            Model = "sdxl",
            Prompt = "make it night",
            NegativePrompt = "people",
            Size = "1024x1024",
            Count = 2,
            Image = Picture(),
            ImageContentType = "image/png",
            Mask = Picture(),
            MaskContentType = "image/png"
        });

        Assert.EndsWith("v1/images/edits", handler.Requests[0].RequestUri!.ToString());

        var body = handler.RequestBodies[0];
        var image = body.IndexOf("name=image", StringComparison.Ordinal);

        Assert.True(image > 0, "the image part is missing");
        foreach (var field in new[] { "name=model", "name=prompt", "name=negative_prompt", "name=n", "name=size" })
        {
            Assert.InRange(body.IndexOf(field, StringComparison.Ordinal), 0, image);
        }

        Assert.Contains("name=mask", body);
    }

    /// <summary>
    /// The synchronous routes are the operation, so they must not also send one: the hub's job route
    /// is the only place <c>operation</c> means anything.
    /// </summary>
    [Fact]
    public async Task The_v1_routes_send_no_operation_field()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);

        await client.CreateVariationAsync(new ImageVariationRequest { Model = "sdxl", Image = Picture() });

        Assert.EndsWith("v1/images/variations", handler.Requests[0].RequestUri!.ToString());
        Assert.DoesNotContain("name=operation", handler.RequestBodies[0]);
    }

    /// <summary>
    /// Rule 5: the part travels under its role. What somebody called the file on their disk is
    /// metadata about their day, and the hub drops it too.
    /// </summary>
    [Fact]
    public async Task An_uploaded_picture_travels_under_its_role_and_never_under_a_caller_filename()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);

        await client.EditAsync(new ImageEditRequest
        {
            Model = "sdxl",
            Prompt = "make it night",
            Image = Picture(),
            ImageContentType = "image/png"
        });

        Assert.Contains("filename=image", handler.RequestBodies[0]);
    }

    // ---- the job seam ----------------------------------------------------------------------

    [Fact]
    public async Task SubmitAsync_posts_json_to_the_jobs_route_and_reads_the_place_in_line()
    {
        var (client, handler) = CreateClient(HttpStatusCode.Accepted, DerivedQueuedJob);

        var job = await client.SubmitAsync(Generation());

        Assert.EndsWith("api/images/jobs", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(MediaJobStates.Queued, job.State);
        Assert.Equal(3, job.QueuePosition);
        Assert.Equal("image", job.Capability);
        Assert.False(job.IsTerminal);

        // Nothing to fetch, and the shape says so rather than the state name plus a rule.
        Assert.Null(job.Images);
    }

    [Fact]
    public async Task A_multipart_job_names_its_operation_because_the_hub_refuses_to_guess()
    {
        var (edit, editHandler) = CreateClient(HttpStatusCode.Accepted, DerivedQueuedJob);
        await edit.SubmitAsync(new ImageEditRequest
        {
            Model = "sdxl",
            Prompt = "make it night",
            Image = Picture()
        });

        Assert.Contains("name=operation", editHandler.RequestBodies[0]);
        Assert.Contains("edit", editHandler.RequestBodies[0]);

        var (variation, variationHandler) = CreateClient(HttpStatusCode.Accepted, DerivedQueuedJob);
        await variation.SubmitAsync(new ImageVariationRequest { Model = "sdxl", Image = Picture() });

        Assert.Contains("variation", variationHandler.RequestBodies[0]);
    }

    [Fact]
    public async Task ListJobsAsync_reads_the_queue_numbers_beside_an_empty_list()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, RecordedEmptyJobList);

        var list = await client.ListJobsAsync();

        Assert.EndsWith("api/images/jobs", handler.Requests[0].RequestUri!.ToString());
        Assert.Empty(list.Jobs);
        Assert.Equal(0, list.Queued);
        Assert.Equal(300, list.RetentionSeconds);

        // It changes what every other number here means: "gone on restart" and "held for the
        // window, restart or not" are different promises.
        Assert.Equal("none", list.Persistence);
    }

    [Fact]
    public async Task GetJobAsync_is_null_on_the_404_that_covers_both_missing_and_not_yours()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedJobNotFound);

        Assert.Null(await client.GetJobAsync("11111111-2222-3333-4444-555555555555"));
    }

    [Fact]
    public async Task A_succeeded_job_says_where_its_images_are_and_what_it_cost()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, DerivedSucceededJob);

        var job = await client.GetJobAsync("7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60");

        Assert.NotNull(job);
        Assert.True(job!.IsTerminal);
        Assert.Equal(28, job.TotalSteps);
        Assert.Equal(29.36, job.MegapixelSteps);

        var output = Assert.Single(job.Images!);
        Assert.Equal(0, output.Index);
        Assert.Equal(1481232, output.Bytes);
        Assert.Equal("/api/images/jobs/7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60/content/0", output.Url);

        // A picture has no duration and reports none rather than a zero.
        Assert.Null(output.Seconds);
    }

    /// <summary>
    /// The reason the type is <c>MediaJob</c> and not <c>ImageJob</c>: the hub renders a video job
    /// through the same serializer, and its output points at OpenAI's video route rather than the
    /// images one. A type named for one modality would have to be renamed in phase 11, and a
    /// published type is not renamed.
    /// </summary>
    [Fact]
    public async Task The_same_document_describes_a_video_job_and_points_at_its_own_content_route()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, DerivedVideoJob);

        var job = await client.GetJobAsync("9c2e1a77-8b4d-4e11-a0f3-6d5c4b3a2190");

        Assert.Equal("video", job!.Capability);
        Assert.Equal(5.0, job.Images![0].Seconds);
        Assert.Equal("/v1/videos/vid_9c2e1a77/content", job.Images[0].Url);
    }

    [Fact]
    public async Task A_failed_job_carries_the_workers_own_code_beside_the_sentence()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, DerivedFailedJob);

        var job = await client.GetJobAsync("7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60");

        Assert.Equal(MediaJobStates.Failed, job!.State);
        Assert.Equal("invalid_request", job.ErrorCode);
        Assert.Contains("1152x896", job.Error);
    }

    [Fact]
    public async Task WatchJobAsync_yields_one_job_per_frame_and_stops_at_the_terminal_one()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedJobEventStream, "text/event-stream");

        var seen = new List<MediaJob>();
        await foreach (var job in client.WatchJobAsync("7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60"))
        {
            seen.Add(job);
        }

        Assert.EndsWith("/events", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal(3, seen.Count);
        Assert.Equal(MediaJobStates.Queued, seen[0].State);
        Assert.Equal(7, seen[1].Step);
        Assert.Equal(28, seen[1].TotalSteps);
        Assert.Equal(MediaJobStates.Succeeded, seen[2].State);
        Assert.Single(seen[2].Images!);
    }

    /// <summary>
    /// The hub's keep-alive re-sends the current state every 15 seconds rather than a bare comment,
    /// so a repeated frame is normal and must not end the loop early or be mistaken for progress.
    /// </summary>
    [Fact]
    public async Task A_repeated_keepalive_frame_is_yielded_and_does_not_end_the_watch()
    {
        var stream = Frame("running", DerivedRunningJob)
            + Frame("running", DerivedRunningJob)
            + Frame("succeeded", DerivedSucceededJob);

        var (client, _) = CreateClient(HttpStatusCode.OK, stream, "text/event-stream");

        var states = new List<string>();
        await foreach (var job in client.WatchJobAsync("7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60"))
        {
            states.Add(job.State);
        }

        Assert.Equal([MediaJobStates.Running, MediaJobStates.Running, MediaJobStates.Succeeded], states);
    }

    [Fact]
    public async Task Watching_a_job_that_has_already_finished_yields_one_frame_and_ends()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, Frame("succeeded", DerivedSucceededJob), "text/event-stream");

        var seen = new List<MediaJob>();
        await foreach (var job in client.WatchJobAsync("7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60"))
        {
            seen.Add(job);
        }

        Assert.True(Assert.Single(seen).IsTerminal);
    }

    // ---- read-once content -----------------------------------------------------------------

    [Fact]
    public async Task OpenContentAsync_hands_over_the_stream_with_the_projection_only_it_carries()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, "the-png-bytes", "image/png");
        handler.ResponseHeaders[ImageHeaders.Projection] = ImageProjections.Equirectangular;
        handler.ResponseHeaders[ImageHeaders.SeamRepair] = SeamRepairModes.Blend;
        handler.ResponseHeaders[ImageHeaders.SeamDelta] = "0.014";
        handler.ResponseHeaders[ImageHeaders.SeamDeltaBefore] = "0.19";
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node-1";

        await using var content = await client.OpenContentAsync("7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60", 0);

        Assert.EndsWith("content/0", handler.Requests[0].RequestUri!.ToString());
        Assert.StartsWith("image/png", content.ContentType);

        // A viewer picks a renderer from this rather than from the aspect ratio, and this response
        // is the only place a caller fetching one image can learn it.
        Assert.Equal(ImageProjections.Equirectangular, content.Projection);
        Assert.Equal(SeamRepairModes.Blend, content.SeamRepair);
        Assert.Equal(0.014, content.SeamDelta);
        Assert.Equal(0.19, content.SeamDeltaBefore);
        Assert.Equal("node-1", content.ServedBy);

        using var reader = new StreamReader(content.Image);
        Assert.Equal("the-png-bytes", await reader.ReadToEndAsync());
    }

    /// <summary>
    /// A response with no projection header is not "unknown": every recipe declares one, and a hub
    /// that sent none is a flat picture. Guessing from the aspect ratio is what this whole field
    /// exists to stop.
    /// </summary>
    [Fact]
    public async Task Content_with_no_projection_header_reads_as_flat_and_no_seam_numbers()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, "the-png-bytes", "image/png");

        await using var content = await client.OpenContentAsync("7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60", 0);

        Assert.Equal(ImageProjections.Flat, content.Projection);
        Assert.Null(content.SeamRepair);
        Assert.Null(content.SeamDelta);
    }

    /// <summary>
    /// The one that would destroy a picture. The content route is a GET, which is everything
    /// <see cref="Client.Http.TransientRetryHandler"/> needs to re-send it — and the first read
    /// unlinks the bytes at the hub, so the retry collects a 410 and the only copy is gone.
    /// </summary>
    [Fact]
    public async Task A_read_once_fetch_is_never_retried_even_with_retries_turned_on()
    {
        var attempts = 0;
        var counter = new CountingHandler(() => attempts++)
        {
            InnerHandler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable, RecordedNoImageNode)
        };

        var retry = new Client.Http.TransientRetryHandler(new InferHubClientOptions
        {
            MaxRetryAttempts = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1)
        })
        {
            InnerHandler = counter
        };

        var http = new HttpClient(retry) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubImagesClient(http);

        await Assert.ThrowsAsync<InferHubOpenAiException>(
            () => client.OpenContentAsync("7a1d0b4e-0f2c-4c9a-9d55-3b1a2c4e5f60", 0));

        Assert.Equal(1, attempts);
    }

    /// <summary>
    /// And the control: an ordinary GET with the same options retries, so the test above is about
    /// the marker rather than about retries being off.
    /// </summary>
    [Fact]
    public async Task An_ordinary_get_with_the_same_options_still_retries()
    {
        var attempts = 0;
        var counter = new CountingHandler(() => attempts++)
        {
            InnerHandler = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable, RecordedNoImageNode)
        };

        var retry = new Client.Http.TransientRetryHandler(new InferHubClientOptions
        {
            MaxRetryAttempts = 2,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1)
        })
        {
            InnerHandler = counter
        };

        var http = new HttpClient(retry) { BaseAddress = new Uri("http://localhost:5080/") };
        var client = new InferHubImagesClient(http);

        await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.ListJobsAsync());

        Assert.Equal(3, attempts);
    }

    // ---- the refusals ----------------------------------------------------------------------

    [Fact]
    public async Task A_fleet_with_no_image_node_is_a_503_that_says_come_back_in_thirty_seconds()
    {
        var (client, handler) = CreateClient(HttpStatusCode.ServiceUnavailable, RecordedNoImageNode);
        handler.ResponseHeaders["Retry-After"] = "30";

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(
            () => client.SubmitAsync(Generation("llava:latest")));

        Assert.Equal("capability_unavailable", error.ErrorCode);
        Assert.Equal("api_error", error.ErrorType);
        Assert.Contains("'image'", error.Message);

        // Worth retrying later — which is the whole difference between this and the 404 below, and
        // a caller cannot act on it without the number.
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public async Task An_edit_with_no_edit_node_names_the_other_capability()
    {
        var (client, _) = CreateClient(HttpStatusCode.ServiceUnavailable, RecordedNoImageEditNode);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.SubmitAsync(new ImageEditRequest
        {
            Model = "llava:latest",
            Prompt = "make it night",
            Image = Picture()
        }));

        Assert.Contains("'image-edit'", error.Message);
    }

    [Fact]
    public async Task A_model_no_node_holds_is_a_404_with_no_retry_after()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedModelNotFound);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.SubmitAsync(Generation()));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("model_not_found", error.ErrorCode);
        Assert.Equal("model", error.Param);
        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public async Task Asking_for_a_url_is_refused_with_the_reason_the_hub_has_no_store()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, RecordedUrlFormatRefused);

        var request = Generation();
        request.ResponseFormat = ImageResponseFormats.Url;

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.GenerateAsync(request));

        Assert.Equal("response_format", error.Param);
        Assert.Contains("never stores a generated image", error.Message);
    }

    [Fact]
    public async Task A_size_that_is_not_a_multiple_of_eight_is_refused_before_a_step_runs()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, RecordedSizeNotMultipleOfEight);

        var request = Generation();
        request.Size = "1001x1000";

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.GenerateAsync(request));

        Assert.Equal("size", error.Param);
        Assert.Contains("multiple of 8", error.Message);
    }

    [Fact]
    public async Task A_batch_over_the_ceiling_names_the_setting()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, RecordedBatchTooLarge);

        var request = Generation();
        request.Count = 9;

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.GenerateAsync(request));

        Assert.Equal("n", error.Param);
        Assert.Contains("Images:MaxBatch", error.Message);
    }

    /// <summary>
    /// The header refusals name a <em>header</em> in <c>param</c>, not a body field. Surfaced
    /// verbatim: a client that mapped it onto its own property names would point the caller at
    /// something that does not exist.
    /// </summary>
    [Theory]
    [InlineData(RecordedBadSteps, ImageHeaders.Steps)]
    [InlineData(RecordedBadStrength, ImageHeaders.Strength)]
    [InlineData(RecordedBadSeamRepair, ImageHeaders.SeamRepair)]
    [InlineData(RecordedBadMaskConvention, ImageHeaders.MaskConvention)]
    public async Task A_refused_extension_header_blames_the_header_by_name(string body, string header)
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, body);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.GenerateAsync(Generation()));

        Assert.Equal(header, error.Param);
        Assert.Equal("invalid_request_error", error.ErrorType);

        // A real error with a null code — a client that treated a missing code as "not an OpenAI
        // envelope" would lose the message.
        Assert.Null(error.ErrorCode);
    }

    /// <summary>
    /// Both of these are recorded 400s from the hub, and neither can be written in C#: a variation
    /// has an image and nothing else. That is what <see cref="ImageVariationRequest"/> is for.
    /// </summary>
    [Fact]
    public void The_refusals_a_variation_earns_are_unreachable_from_this_client()
    {
        Assert.Contains("takes no prompt", RecordedVariationWithPrompt);
        Assert.Contains("takes no mask", RecordedVariationWithMask);

        var properties = typeof(ImageVariationRequest).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("Prompt", properties);
        Assert.DoesNotContain("Mask", properties);
    }

    /// <summary>
    /// Recorded, and unreachable for the same kind of reason: this client always names the operation
    /// on a multipart job, because defaulting it would let a typo turn a variation into an edit.
    /// </summary>
    [Fact]
    public void A_multipart_job_without_an_operation_is_a_refusal_this_client_never_provokes()
        => Assert.Contains("must name its operation", RecordedMultipartWithoutOperation);

    [Fact]
    public async Task An_edit_with_no_prompt_throws_before_a_request_goes_out()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.EditAsync(new ImageEditRequest { Model = "sdxl", Image = Picture() }));

        Assert.Empty(handler.Requests);
        Assert.Contains("an edit says what to change", RecordedEditWithoutPrompt);
    }

    [Fact]
    public async Task An_edit_with_no_picture_throws_before_a_request_goes_out()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.EditAsync(new ImageEditRequest { Model = "sdxl", Prompt = "make it night" }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_generation_with_no_prompt_throws_before_a_request_goes_out()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSyncEnvelope);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GenerateAsync(new ImageGenerationRequest { Model = "sdxl" }));

        Assert.Empty(handler.Requests);
    }

    /// <summary>Counts what actually reached the transport, for the retry tests.</summary>
    private sealed class CountingHandler : DelegatingHandler
    {
        private readonly Action onSend;

        public CountingHandler(Action onSend) => this.onSend = onSend;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend();
            return base.SendAsync(request, cancellationToken);
        }
    }
}
