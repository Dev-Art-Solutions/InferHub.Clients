InferHub.Client 1.2.0 — the C# client **speaks and listens**: transcription with segments and
subtitles, and a synthesis you can play before it is finished. Additive throughout: no signature
changed, no property was renamed, no published interface gained a member, and code written against
1.1.x compiles and behaves identically.

## Audio

`IInferHubAudioClient` — same base address, same client key, registered by the same
`AddInferHubClient(...)`:

| Method | Endpoint |
|---|---|
| `TranscribeAsync` | `POST /v1/audio/transcriptions` — forces `verbose_json`, returns a parsed `Transcription` |
| `TranscribeDocumentAsync` | `POST /v1/audio/transcriptions` — `text` / `srt` / `vtt`, returned verbatim |
| `CreateSpeechAsync` | `POST /v1/audio/speech` — the whole file, **or** `stream_format: "audio"` |
| `StreamSpeechAsync` | `POST /v1/audio/speech` with `stream_format: "sse"` → `IAsyncEnumerable<SpeechChunk>` |

A **third interface** rather than four more methods on `IInferHubOpenAiClient`, even though audio is
served in the `/v1` dialect: that interface shipped in 1.1.0, and a new member on a shipped
interface breaks every implementer holding a test double. The 1.x contract outranks the tidiness of
one interface per dialect, and root `CLAUDE.md` rule 9 has been amended to say so.

### The buffered call and the streamed one are the same method

```csharp
await using var speech = await audio.CreateSpeechAsync(SpeechRequest.Create("piper", "Hello."));
await using var file = File.Create("speech.wav");
await speech.Audio.CopyToAsync(file);
```

Set `StreamFormat = SpeechStreamFormats.Audio` and **not one byte of that changes** — the hub writes
the file as it is made instead of all at once, and the caller reads it sooner. That falls out of the
rule this library already had: read-once and long content is a **stream the caller owns**, never a
`byte[]` allocated to be friendly. `ReadAllBytesAsync()` is there, on top of the stream, for whoever
genuinely wanted bytes.

### The framed form, and a zero that is a measurement

```csharp
await foreach (var chunk in audio.StreamSpeechAsync(request))
{
    if (chunk.Usage is { } usage) { Console.WriteLine(chunk.Characters); continue; }
    await player.WriteAsync(chunk.Audio);
}
```

The terminal `speech.audio.done` is **yielded, not swallowed** — the same shape as the choice-less
usage frame on `/v1/chat/completions`, so a caller who learned that rule once has learned this one.
Its three token counts are **zero and true**: a phoneme model tokenized nothing. The number that
reconciles with a bill is `Characters`, from `X-InferHub-Speech-Characters`. `ServedBy`,
`SampleRate` and `Characters` are read once from the headers and stamped on every chunk — and for
`pcm`, which is headerless by definition, `SampleRate` is the only place the rate exists.

A `speech.audio.error` frame — the hub's own extension for a stream that died after the caller
already held a `200` — is raised as `InferHubOpenAiException`. A partial answer plus a clean
exception, and nothing is ever retried.

### Two things the wire does that a schema would not have told you

- **Every form field is written before the file part.** Above the hub's `Tools:MaxStreamedBytes` the
  request is routed from the leading fields while the bytes are still arriving, so a field after the
  file is a `400` — and a `model` the hub never saw would be a transcription answered by the wrong
  node. The buffered path below that ceiling tolerates any order, **which is what makes it
  dangerous**: getting it wrong is correct on every test recording and wrong on the first real file.
- **`503` + `capability_unavailable` is not `404`.** "The fleet holds this model but no node is
  currently doing this kind of work" carries `Retry-After`; "no node holds the model" does not.
  Catch on `ErrorCode`, not on the status alone.

Also: `error.param` names the field the hub blames, and the two routes blame different ones — a
refused `response_format` comes back as `param: "model"` from transcriptions and `param: "input"`
from speech. Surfaced verbatim rather than mapped onto our own property names.

## Transcription

```csharp
await using var input = File.OpenRead("meeting.wav");   // the client never disposes your stream

var transcript = await audio.TranscribeAsync(
    TranscriptionRequest.FromStream("whisper-1", input, "meeting.wav", "audio/wav"));
```

`TranscribeAsync` always asks for `verbose_json` — text, language, duration and segments — because
that is the shape a C# caller does something with. For a subtitle file, `TranscribeDocumentAsync`
sends your `ResponseFormat` and returns **the hub's own bytes untouched**: an `srt` is a file, and
reinterpreting it into a transcript object would lose the cue timings that were the reason to ask.

**No encoder, no resampler, no audio library.** A format the fleet's worker cannot produce is the
hub's `400` naming what it can do — never a quiet substitution, which is how a caller ends up with a
corrupted file carrying a confident content type.

**No `InferHubCallOptions` overload on either route**, deliberately: audio dispatches to a node that
declared the capability and reads neither the provider steer nor the conversation or retrieval
headers. An overload would compile, send a header nothing reads, and be a documented feature that
does not work.

## Under the hood

The SSE **line** mechanics moved to one internal reader both dialects use (`data:` accumulation,
comments, the blank-line boundary, a last frame at EOF). How a stream **ends** stayed per surface —
`[DONE]` on `/v1/chat/completions`, a named terminal event here, no sentinel at all on the raw form.
One loop with a mode flag would be two loops sharing a bug. No public API changed.

The audio client is registered with an **infinite `HttpClient.Timeout`** — a streamed synthesis
outlives the 100-second default and an `HttpClient` timeout would abort it mid-sentence — and
applies `Options.Timeout` per transcription instead. The same shape the admin SSE stream already
needed.

## Dependencies, size, tests

- **Dependency budget unchanged**: `Microsoft.Extensions.Http` and
  `Microsoft.Extensions.DependencyInjection.Abstractions`. Multipart is `MultipartFormDataContent`
  from the BCL; SSE stays hand-rolled.
- `<IsAotCompatible>` still true, zero trim/AOT warnings, Release build clean with `CS1591` as
  error, `dotnet format --verify-no-changes` clean.
- **125 tests per target framework (net9.0 and net10.0): 122 pass, 3 skip** — the skips are the
  env-gated integration suite, which runs only with `INFERHUB_TEST_BASEADDRESS` set. Skipped is not
  passed. 1.1.0 had 104 per TFM.
- New sample: `dotnet/samples/Speech` — synthesise framed, write the wav, synthesise buffered, then
  transcribe a file and render its `srt`.

## What this release does **not** establish, said out loud

This is the important section this time.

- **No successful transcription and no synthesised audio was ever observed.** The hub available on
  the day — InferHub 3.37.0, one node — serves `chat` and `embed` with `tools.enabled=false`, so
  there was no `transcribe` or `speak` node to reach. Nothing is claimed here about audio quality,
  sample rates in practice, first-chunk latency, or how a real worker chunks a sentence.
- **What *was* driven against that live hub — from the published package, not the working copy.**
  `dotnet add package InferHub.Client --version 1.2.0` into a clean directory pinned to nuget.org,
  then seven checks against InferHub 3.37.0:

  | | result |
  |---|---|
  | `IInferHubAudioClient` from DI **and** from its public constructor | resolves |
  | `StreamSpeechAsync` | `503 api_error / capability_unavailable` |
  | `CreateSpeechAsync` | `503 api_error / capability_unavailable` |
  | `TranscribeAsync` — the multipart body this client builds | `503 … 'transcribe'` |
  | speech, unknown model | `404 not_found_error / model_not_found` |
  | speech, `response_format: "aiff"` | `400`, `param=input`, `code=null`, the list of what it can do |
  | **streamed chat on the 1.1 surface** | *"Hello! How may I assist you today?"*, `ServedBy: node`, 38 tokens |

  Reaching *routing* is the part that matters for the two audio routes: it means the real hub
  parsed the multipart form and read `model` out of it, so **the request shapes and the whole
  failure path are established end to end** — only the answer is not. The last row is the
  regression check the shared-SSE-reader refactor needed, and it is a real streamed answer with a
  real usage frame.
- **Every *refusal* in the test suite is recorded from that live hub** — eleven of them: both
  `capability_unavailable` `503`s (transcribe and speak), the `model_not_found` `404`, the two
  unsupported-`response_format` `400`s with their differing `param`, the un-streamable-format `400`,
  the unknown-`stream_format` `400`, the empty-`input` `400`, the missing-`file` and missing-`model`
  `400`s, and the "this endpoint takes multipart/form-data" `400`. The last four are unreachable
  from C# because the client's own guards fire first; they are kept as recorded bodies for the
  phase-15 corpus and to prove the hub's sentence reaches a caller unflattened.
- **The three *success* shapes are derived, not recorded, and each is marked as such in the test
  file with its reason** — `speech.audio.delta` / `speech.audio.done`, the `verbose_json` envelope,
  and the SubRip rendering. They are taken from the hub serializers that write those bytes
  (`SpeechStream`, `VerboseTranscription`, `TranscriptFormatter.ToSrt`) rather than from what a
  client author expects. Derived is weaker than recorded; phase 25 is where a real one arrives.
- **The `speech.audio.error` path has never been seen in the wild** either — the frame shape is the
  hub's `SpeechStream.Error`, and the test drives the client with it.
- **`conformance/cases.json` still does not exist** — phase 15. The four shapes this phase learned
  are written into `spec/README.md` under "the shapes that have actually broken clients".
- Images and the job seam, video, ingestion and search, the admin catch-up, and the node as a target
  remain phases 10–14.

## Install

```
dotnet add package InferHub.Client --version 1.2.0
```
