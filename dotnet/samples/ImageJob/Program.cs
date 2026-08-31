// Submit an image job, watch it render step by step, and collect the picture — which the hub
// hands over exactly once.
//
//   INFERHUB_BASE        coordinator or solo node address (default http://localhost:5080/)
//   INFERHUB_API_KEY     client key, if the hub requires one
//   INFERHUB_IMAGE_MODEL recipe id (default sdxl)
//   INFERHUB_IMAGE_OUT   where to write the png (default ./inferhub-image.png)

using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Images;
using Microsoft.Extensions.DependencyInjection;

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");
var model = Environment.GetEnvironmentVariable("INFERHUB_IMAGE_MODEL") ?? "sdxl";
var output = Environment.GetEnvironmentVariable("INFERHUB_IMAGE_OUT") ?? "inferhub-image.png";

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = baseAddress;
    o.ApiKey = apiKey;
});

using var provider = services.BuildServiceProvider();
var images = provider.GetRequiredService<IInferHubImagesClient>();

Console.WriteLine($"Coordinator: {baseAddress}");
Console.WriteLine($"Model:       {model}");
Console.WriteLine();

try
{
    var queue = await images.ListJobsAsync();
    Console.WriteLine($"queue: {queue.Queued} waiting, {queue.Active} running, "
        + $"results kept {queue.RetentionSeconds}s (persistence: {queue.Persistence})");

    var job = await images.SubmitAsync(new ImageGenerationRequest
    {
        Model = model,
        Prompt = "a lighthouse in fog, long exposure",
        Size = "1024x1024",
        Options = new ImageOptions { Steps = 28, Seed = 42 }
    });

    Console.WriteLine($"job {job.Id}: {job.State}"
        + (job.QueuePosition is { } place ? $", #{place} in line" : string.Empty));

    await foreach (var progress in images.WatchJobAsync(job.Id))
    {
        Console.WriteLine(progress.Step is { } step
            ? $"  {progress.State} — step {step}/{progress.TotalSteps} on {progress.Node}"
            : $"  {progress.State}");

        job = progress;
    }

    if (job.State != MediaJobStates.Succeeded)
    {
        Console.WriteLine($"[{job.State}] {job.Error} ({job.ErrorCode})");
        return;
    }

    foreach (var warning in job.Warnings ?? [])
    {
        Console.WriteLine($"  warning: {warning}");
    }

    // Read-once: this fetch unlinks the bytes at the hub. Write them somewhere before anything
    // else can go wrong, and never "retry" a failed download — there is nothing left to retry.
    await using var content = await images.OpenContentAsync(job.Id, 0);
    await using (var file = File.Create(output))
    {
        await content.Image.CopyToAsync(file);
    }

    Console.WriteLine($"wrote {output} — {content.ContentType}, projection {content.Projection}"
        + (content.SeamRepair is { } repair ? $", seam repaired by {repair} ({content.SeamDeltaBefore:F3} → {content.SeamDelta:F3})" : string.Empty));
    Console.WriteLine($"served by {content.ServedBy}, metered {job.MegapixelSteps:F1} megapixel-steps");
}
catch (InferHubOpenAiException ex) when (ex.ErrorCode == "capability_unavailable")
{
    // The fleet holds the model and no node is currently rendering. Not a 404, and worth retrying.
    Console.WriteLine($"[503] {ex.Message}");
    Console.WriteLine($"      try again in {ex.RetryAfter?.TotalSeconds ?? 30:F0}s");
}
catch (InferHubOpenAiException ex)
{
    Console.WriteLine($"[image error {(int)ex.StatusCode} {ex.ErrorCode}] {ex.Message}"
        + (ex.Param is { } param ? $" (param: {param})" : string.Empty));
}
catch (InferHubException ex)
{
    Console.WriteLine($"[image error {(int)ex.StatusCode}] {ex.Message}");
}
