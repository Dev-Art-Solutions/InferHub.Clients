using InferHub.Client;
using InferHub.Client.Exceptions;
using InferHub.Client.Extensions;
using InferHub.Client.Models.Audio;
using Microsoft.Extensions.DependencyInjection;

// Speak a sentence, then transcribe a file if one is given.
//
//   INFERHUB_BASE          coordinator or solo node, default http://localhost:5080/
//   INFERHUB_API_KEY       client key (loopback usually needs none)
//   INFERHUB_SPEECH_MODEL  a model on a node that declares `speak`, default "piper"
//   INFERHUB_STT_MODEL     a model on a node that declares `transcribe`, default "whisper-1"
//   INFERHUB_AUDIO         path to an audio file; omit to skip the transcription half
//
// A fleet with no such node answers 503 with code `capability_unavailable` and a Retry-After,
// which is a different thing from a 404 and is printed as such below.

var baseAddress = new Uri(Environment.GetEnvironmentVariable("INFERHUB_BASE") ?? "http://localhost:5080/");
var apiKey = Environment.GetEnvironmentVariable("INFERHUB_API_KEY");
var speechModel = Environment.GetEnvironmentVariable("INFERHUB_SPEECH_MODEL") ?? "piper";
var sttModel = Environment.GetEnvironmentVariable("INFERHUB_STT_MODEL") ?? "whisper-1";
var audioPath = Environment.GetEnvironmentVariable("INFERHUB_AUDIO");

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = baseAddress;
    o.ApiKey = apiKey;
});

using var serviceProvider = services.BuildServiceProvider();
var audio = serviceProvider.GetRequiredService<IInferHubAudioClient>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Coordinator: {baseAddress}");
Console.WriteLine();

try
{
    // ---- speech, framed, so the first sentence is playable before the last one exists ----------
    Console.WriteLine($"Synthesising with '{speechModel}' (stream_format: sse)…");

    var output = new FileStream("speech.wav", FileMode.Create, FileAccess.Write);
    var frames = 0;

    await using (output)
    {
        await foreach (var chunk in audio.StreamSpeechAsync(
            SpeechRequest.Create(speechModel, "Hello from the fleet.", responseFormat: SpeechFormats.Wav),
            cts.Token))
        {
            // The terminal frame carries a count and no audio. Its three zeros are a true count —
            // a phoneme model tokenized nothing — and the number that reconciles with a bill is
            // the character count on the header.
            if (chunk.Usage is { } usage)
            {
                Console.WriteLine($"  done: {frames} frame(s), {chunk.SampleRate} Hz, "
                    + $"{chunk.Characters} characters metered, {usage.TotalTokens} tokens");
                Console.WriteLine($"  served by: {chunk.ServedBy ?? "(no header)"}");
                continue;
            }

            frames++;
            await output.WriteAsync(chunk.Audio, cts.Token);
        }
    }

    Console.WriteLine("  wrote speech.wav");
    Console.WriteLine();

    // ---- and the same thing unframed: one call, whether the hub buffers it or not --------------
    await using (var whole = await audio.CreateSpeechAsync(
        SpeechRequest.Create(speechModel, "And again, in one file."), cts.Token))
    {
        await using var file = File.Create("speech-buffered" + Path.GetExtension(whole.FileName ?? ".wav"));
        await whole.Audio.CopyToAsync(file, cts.Token);
        Console.WriteLine($"  wrote {file.Name} ({whole.ContentType})");
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("[cancelled]");
}
catch (InferHubOpenAiException ex)
{
    Report(ex);
}

if (audioPath is null)
{
    Console.WriteLine();
    Console.WriteLine("Set INFERHUB_AUDIO=<file> to transcribe as well.");
    return;
}

Console.WriteLine();
Console.WriteLine($"Transcribing {Path.GetFileName(audioPath)} with '{sttModel}'…");

try
{
    // The client does not own the stream and never disposes it — open it, call, dispose it here.
    await using var input = File.OpenRead(audioPath);

    var transcript = await audio.TranscribeAsync(
        TranscriptionRequest.FromStream(sttModel, input, Path.GetFileName(audioPath), "audio/wav"),
        cts.Token);

    Console.WriteLine($"  {transcript.Text}");
    Console.WriteLine($"  language: {transcript.Language ?? "(not reported)"}, "
        + $"duration: {transcript.Duration?.ToString("0.0") ?? "(not reported)"}s, "
        + $"segments: {transcript.Segments.Count}");
    Console.WriteLine($"  served by: {transcript.ServedBy ?? "(no header)"}");

    if (transcript.Segments.Count > 0)
    {
        // Subtitles come back as the hub rendered them, bytes untouched — a comma before the
        // milliseconds in SubRip, a period in WebVTT, and getting that wrong is a file one player
        // accepts and another does not.
        input.Position = 0;
        var request = TranscriptionRequest.FromStream(sttModel, input, Path.GetFileName(audioPath), "audio/wav");
        request.ResponseFormat = TranscriptionFormats.Srt;

        var srt = await audio.TranscribeDocumentAsync(request, cts.Token);
        await File.WriteAllTextAsync("transcript.srt", srt.Content, cts.Token);
        Console.WriteLine($"  wrote transcript.srt ({srt.ContentType})");
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("[cancelled]");
}
catch (InferHubOpenAiException ex)
{
    Report(ex);
}

static void Report(InferHubOpenAiException ex)
{
    // 503 + capability_unavailable is "the fleet has the model, no node is doing this kind of work
    // right now" and carries a Retry-After; 404 is "no node holds the model at all". Two different
    // things to do about it, so they are printed as two different things.
    Console.WriteLine(ex.ErrorCode == "capability_unavailable"
        ? $"[{(int)ex.StatusCode}] {ex.Message} — no node currently provides this capability."
        : $"[{(int)ex.StatusCode} {ex.ErrorType}] {ex.Message}");
}
