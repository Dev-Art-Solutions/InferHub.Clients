// Ask for a clip, watch it render, and collect the mp4 — which the hub hands over exactly once.
//
//   INFERHUB_BASE        coordinator or solo node address (default http://localhost:5080/)
//   INFERHUB_API_KEY     client key, if the hub requires one
//   INFERHUB_VIDEO_MODEL recipe id (default wan2.2)
//   INFERHUB_VIDEO_OUT   where to write the clip (default ./inferhub-video.mp4)

using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Videos;
using Microsoft.Extensions.DependencyInjection;

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");
var model = Environment.GetEnvironmentVariable("INFERHUB_VIDEO_MODEL") ?? "wan2.2";
var output = Environment.GetEnvironmentVariable("INFERHUB_VIDEO_OUT") ?? "inferhub-video.mp4";

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = baseAddress;
    o.ApiKey = apiKey;
});

using var provider = services.BuildServiceProvider();
var video = provider.GetRequiredService<IInferHubVideoClient>();

Console.WriteLine($"Coordinator: {baseAddress}");
Console.WriteLine($"Model:       {model}");
Console.WriteLine();

try
{
    // One queue for both long modalities, so these numbers count pictures as well as clips.
    var queue = await video.ListJobsAsync();
    Console.WriteLine($"queue: {queue.Queued} waiting, {queue.Active} running, "
        + $"results kept {queue.RetentionSeconds}s (persistence: {queue.Persistence})");

    var clip = await video.CreateAsync(new VideoGenerationRequest
    {
        Model = model,
        Prompt = "a kite over a grey sea, handheld, overcast",

        // 832x480, not 1920x1080: a video pipeline downsamples by 16, so 1080 is a 400 here.
        Size = VideoSizes.Wide480,
        Seconds = 5,
        Options = new VideoOptions { Steps = 30, Seed = 42 }
    });

    Console.WriteLine($"{clip.Id}: {clip.Status}");

    // A poll, not a stream: this dialect has no events route. Progress is capped at 99 until the
    // render is over, so the loop ends on the terminal document rather than on 100.
    await foreach (var progress in video.WatchAsync(clip.Id))
    {
        Console.WriteLine($"  {progress.Status} — {progress.Progress}%");
        clip = progress;
    }

    if (clip.Status != VideoStatuses.Completed)
    {
        Console.WriteLine($"[{clip.Status}] {clip.Error?.Message} ({clip.Error?.Code})");
        return;
    }

    Console.WriteLine($"{clip.Size}, {clip.Seconds:F2}s — fetchable until {clip.Expires:HH:mm:ss}");

    // Read-once: this fetch unlinks the bytes at the hub. Write them somewhere before anything else
    // can go wrong, and never "retry" a failed download — there is nothing left to retry.
    await using var content = await video.OpenContentAsync(clip.Id);
    await using (var file = File.Create(output))
    {
        await content.Video.CopyToAsync(file);
    }

    Console.WriteLine($"wrote {output} — {content.ContentType}, {content.ContentLength:N0} bytes, served by {content.ServedBy}");
}
catch (InferHubOpenAiException ex) when (ex.ErrorCode == VideoErrorCodes.CapabilityUnavailable)
{
    // The fleet holds the model and no node is currently rendering video. Not a 404, and worth
    // retrying — which is the whole reason the two are different codes.
    Console.WriteLine($"[503] {ex.Message}");
    Console.WriteLine($"      try again in {ex.RetryAfter?.TotalSeconds ?? 30:F0}s");
}
catch (InferHubOpenAiException ex) when (ex.ErrorCode == VideoErrorCodes.Expired)
{
    // The bytes existed and are gone: read, evicted, or retention lapsed. Not "there was no clip".
    Console.WriteLine($"[410] {ex.Message}");
}
catch (InferHubOpenAiException ex)
{
    Console.WriteLine($"[video error {(int)ex.StatusCode} {ex.ErrorCode}] {ex.Message}"
        + (ex.Param is { } param ? $" (param: {param})" : string.Empty));
}
catch (InferHubException ex)
{
    Console.WriteLine($"[video error {(int)ex.StatusCode}] {ex.Message}");
}
