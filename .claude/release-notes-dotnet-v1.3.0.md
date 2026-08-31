InferHub.Client 1.3.0 — the C# client **draws**: the three synchronous Images routes, and the async
job seam under them, with per-step progress over SSE and content the hub hands over exactly once.
Additive throughout: no signature changed, no property was renamed, no published interface gained a
member, and code written against 1.2.x compiles and behaves identically.

## Images

`IInferHubImagesClient` — same base address, same client key, registered by the same
`AddInferHubClient(...)`:

| Method | Endpoint |
|---|---|
| `GenerateAsync` | `POST /v1/images/generations` — submit and wait |
| `EditAsync` | `POST /v1/images/edits` (multipart: picture, prompt, optional mask) |
| `CreateVariationAsync` | `POST /v1/images/variations` (multipart: picture only) |
| `SubmitAsync` | `POST /api/images/jobs` — the same three requests, as a job |
| `ListJobsAsync` | `GET /api/images/jobs` — this client's jobs, plus the queue's own depth |
| `GetJobAsync` | `GET /api/images/jobs/{id}` → `null` on 404 |
| `WatchJobAsync` | `GET /api/images/jobs/{id}/events` (SSE → `IAsyncEnumerable<MediaJob>`) |
| `OpenContentAsync` | `GET /api/images/jobs/{id}/content/{index}` — **read once** |
| `CancelJobAsync` | `DELETE /api/images/jobs/{id}` |

A **fourth interface** rather than nine more methods on `IInferHubOpenAiClient`, even though three of
these routes are `/v1`: that interface shipped in 1.1.0, and a new member on a shipped interface
breaks every implementer holding a test double. Rule 9, decided the same way audio was.

### Synchronous and asynchronous are the same request

```csharp
var answer = await images.GenerateAsync(new ImageGenerationRequest
{
    Model = "sdxl", Prompt = "a lighthouse in fog", Size = "1024x1024",
    Options = new ImageOptions { Steps = 28, Seed = 42 }
});
```

What differs is whether you wait. A synchronous call queues in the same line as every job, and past
the hub's own `Images:SyncMaxWaitSeconds` it answers `503` with `ErrorCode == "job_still_running"` —
the render carries on, and the message names the job. So `SubmitAsync` takes the *same* request
records and hands back the job immediately instead:

```csharp
var job = await images.SubmitAsync(request);

await foreach (var progress in images.WatchJobAsync(job.Id))   // ends on the terminal frame
{
    Console.WriteLine($"{progress.State} {progress.Step}/{progress.TotalSteps}");
}
```

Each SSE frame is **the whole job document**, so `state`, `step` and `totalSteps` are read off it
with no second request. There is no `[DONE]` sentinel of any kind — the stream simply ends after the
terminal frame — so this client keys on the payload's own state and stops there. The hub's 15-second
keep-alive re-sends the current state rather than a comment, which is deliberate: a client that
reconnected mid-render needs no catch-up `GET`.

### Read-once content, and the retry that would have destroyed a picture

```csharp
await using var content = await images.OpenContentAsync(job.Id, 0);
await using var file = File.Create("lighthouse.png");
await content.Image.CopyToAsync(file);         // ReadAllBytesAsync() exists, on top of this
```

The read **unlinks the bytes at the hub**. A second fetch is a `410 job_expired`, and so is a
*retried* one — and that route is a `GET`, which is everything `TransientRetryHandler` needs to
re-send it after a dropped connection and collect a `410` where the picture used to be. So the
request is **marked never-retry at the handler**, whatever `MaxRetryAttempts` the caller configured.
Two tests hold that down: the content fetch reaches the transport exactly once with retries set to
3, and an ordinary `GET` with the same options still retries, so the first test is about the marker
rather than about retries being off.

`content.Projection` is the one thing only that response carries — `flat` or `equirectangular`,
**declared by the worker, never inferred from the aspect ratio**, because a 2:1 photograph and a 2:1
panorama are the same bytes in the same shape and only one of them opens correctly in a headset.
The seam numbers ride beside it when a repair was asked for, and `SeamDeltaBefore == SeamDelta` is a
real outcome: the repair ran, it did not help, and the hub discarded it.

### `MediaJob`, not `ImageJob`

The hub renders **video** jobs through the same serializer — the same fields, `capability` telling
them apart, and each output's `url` already pointing at its own content route
(`/v1/videos/{id}/content` for a clip). Naming the type for one modality would mean renaming it in
1.4.0, and a published type is not renamed. A test drives a real video job document through this
client to hold that claim down.

### Two refusals you cannot write

`ImageEditRequest` and `ImageVariationRequest` are separate types because the hub answers
`400 "a variation takes no prompt — it is 'more of this picture'"` and
`400 "a variation takes no mask"`. One record with nullable fields makes both expressible and
discoverable only over the network; two records make them unrepresentable. For image-to-image *with*
a prompt, that is an edit with no mask — which is what the hub's own refusal tells you to do.

### The knobs travel as headers, invariantly

`steps`, `guidance`, `seed`, `strength`, the mask convention and seam repair are
`X-InferHub-Image-*` headers rather than body fields, gathered into one `ImageOptions` object. Every
number leaves it formatted with `InvariantCulture`: a `strength` of 0.75 sent as `0,75` from a
Bulgarian or German machine is a `400` from the hub, and it is a `400` that only reproduces on some
developers' laptops. A test sets the thread culture to `bg-BG` and asserts the wire.

Refusals from those headers name **the header** in `error.param` — `param:
"X-InferHub-Image-Steps"` — and carry `code: null` at the same time. Surfaced verbatim: a client
that mapped `param` onto its own property names would point a caller at a field that does not exist,
and one that required a `code` to recognise an OpenAI envelope would lose the sentence entirely.

## Also in this release

`InferHubException.RetryAfter` — an `init`-only property, no constructor touched, so nothing compiled
against 1.2.0 changes. Three refusals on this surface carry a `Retry-After` and are useless without
it: `503 capability_unavailable` (the fleet holds the model, no node is rendering — **not** a `404`,
and worth retrying), `503 queue_full`, and `503 job_still_running`. It is read once in the shared
response plumbing rather than in one client, so audio's `capability_unavailable` gets it too.

## Dependencies, size, tests

- **Dependency budget unchanged**: `Microsoft.Extensions.Http` and
  `Microsoft.Extensions.DependencyInjection.Abstractions`. Multipart is `MultipartFormDataContent`
  from the BCL; SSE stays hand-rolled; **nothing here decodes a pixel**, so no image library
  entered the budget.
- `<IsAotCompatible>` still true, zero trim/AOT warnings, Release build clean with `CS1591` as
  error, `dotnet format --verify-no-changes` clean.
- **163 tests per target framework (net9.0 and net10.0): 160 pass, 3 skip** — the skips are the
  env-gated integration suite, which runs only with `INFERHUB_TEST_BASEADDRESS` set. Skipped is not
  passed. 1.2.0 had 125 per TFM.
- New sample: `dotnet/samples/ImageJob` — list the queue, submit, watch it step through, write the
  png, and print what it was metered.

## What this release does **not** establish, said out loud

- **No image was ever generated.** The hub available on the day — InferHub 3.37.0, one node —
  serves `chat` and `embed` with `tools.enabled=false`, so there is no `image` or `image-edit` node
  on it. Nothing is claimed here about render quality, real step timings, what a 4 K download costs,
  or how a repaired seam actually looks.
- **What *was* driven against that live hub — from the published package, not the working copy.**
  `dotnet add package InferHub.Client --version 1.3.0` into a clean directory pinned to nuget.org,
  then fourteen checks against InferHub 3.37.0:

  | | result |
  |---|---|
  | `IInferHubImagesClient` from DI **and** from its public constructor | resolves |
  | `ListJobsAsync` | `0 jobs, queued=0, active=0, retention=300s, persistence=none` |
  | `SubmitAsync(generation)`, model the fleet holds | `503 api_error / capability_unavailable`, **`Retry-After: 30s`** |
  | `SubmitAsync(generation)`, model nobody holds | `404 not_found_error / model_not_found`, `param=model`, **no retry** |
  | `GenerateAsync` with `response_format: "url"` | `400`, `param=response_format`, `code=null` |
  | `GenerateAsync` with `size: "1001x1000"` | `400`, `param=size` |
  | `ImageOptions { Steps = 200 }` | `400`, **`param=X-InferHub-Image-Steps`** |
  | `SubmitAsync(edit)` — the multipart form this client builds | `503 … 'image-edit'` |
  | `SubmitAsync(variation)` | `503 … 'image-edit'` |
  | `GetJobAsync(unknown)` | `null` |
  | `OpenContentAsync(unknown)` | `404 / job_not_found`, `param=id` |
  | `CancelJobAsync(unknown)` | `404 / job_not_found` |
  | `WatchJobAsync(unknown)` | `404 / job_not_found` |
  | **streamed chat on the 1.2 surface** | *"Hello! It's nice to meet you. How may I assist you today?"*, `ServedBy: node` |

  The rows that matter are the seventh, eighth and ninth. The seventh is the real hub reading a
  header this client wrote — so `ImageOptions` is established end to end, invariant formatting
  included. The eighth and ninth are the real hub **parsing the multipart form**, reading
  `operation` and `model` out of it and reaching routing: the request shapes and the whole failure
  path are established, and only the picture is not. (Both edits and variations resolve to the
  `image-edit` capability at the hub, which is why the ninth row names it too.)
- **Every *refusal* in the test suite is recorded from that live hub** — fifteen of them, pasted
  verbatim with their escapes: both `capability_unavailable` `503`s, the `model_not_found` `404`,
  the `job_not_found` `404`, the `response_format=url` `400`, the not-a-multiple-of-8 `400`, the
  `Images:MaxBatch` `400`, four extension-header `400`s (steps, strength, seam repair, mask
  convention), the two variation refusals, the missing-`operation` `400` and the edit-without-prompt
  `400`. The last four are unreachable from C# because the client's types or its own guards fire
  first; they are kept as recorded bodies for the phase-15 corpus and because they are the argument
  for the types.
- **The success shapes are derived, not recorded, and each is marked as such in the test file** —
  the synchronous envelope (flat and repaired-panorama variants), the job document in its queued,
  running, succeeded and failed states, the video job document, and the SSE frame stream. They come
  from the hub serializers that write those bytes — `ImageRenderer.Envelope`,
  `ImageJobView.Describe`, `ImageJobEndpoints.WriteEventAsync`, `SeamRepairModes.HeadersFor` —
  rather than from what a client author expects. Derived is weaker than recorded; phase 25 is where
  a real one arrives. The one success body that *was* recorded is the empty job listing.
- **The read-once path has never destroyed or delivered a real picture**, because there has never
  been one. The never-retry marker is asserted against the transport, not against the hub.
- **`conformance/cases.json` still does not exist** — phase 15. The six shapes this phase learned
  are written into `spec/README.md` under "the shapes that have actually broken clients".
- Video, ingestion and search, the admin catch-up, and the node as a target remain phases 11–14.

## Install

```
dotnet add package InferHub.Client --version 1.3.0
```
