InferHub.Client 1.4.0 — the C# client asks for **video**, in OpenAI's own asynchronous dialect: the
create, the poll, the bytes the hub hands over exactly once, and the listing that dialect does not
have. With it, every modality the hub serves is reachable from C#. Additive throughout: no signature
changed, no property was renamed, no published interface gained a member, and code written against
1.3.x compiles and behaves identically.

## Video

`IInferHubVideoClient` — same base address, same client key, registered by the same
`AddInferHubClient(...)`:

| Method | Endpoint |
|---|---|
| `CreateAsync` | `POST /v1/videos` — accepted immediately, `status: queued` |
| `GetAsync` | `GET /v1/videos/{id}` → `null` on 404 |
| `WatchAsync` | polls `GET /v1/videos/{id}` (→ `IAsyncEnumerable<Video>`) |
| `OpenContentAsync` | `GET /v1/videos/{id}/content` — **read once**, no index |
| `DeleteAsync` | `DELETE /v1/videos/{id}` — cancel **and** drop |
| `ListJobsAsync` | `GET /api/videos/jobs` — this client's video jobs, in the job vocabulary |

A **fifth interface**, for the reason the fourth was one: `IInferHubImagesClient` shipped in 1.3.0,
and a new member on a shipped interface breaks every implementer holding a test double. What *is*
shared is shared in types — `MediaJob` for the listing, `RetryAfter`, the never-retry marker, the
read-once stream shape.

### The two methods this release deliberately does not ship

OpenAI's Videos API has a listing and a remix. This hub refuses both, with the reason in the
sentence, recorded here from a live 3.37.0:

```json
{"error":{"message":"listing videos is not supported: a video id is itself the capability to fetch the bytes, so this API does not hand a caller a way to enumerate other jobs. …","type":"invalid_request_error","param":null,"code":"not_supported"}}
{"error":{"message":"remixing 'video_…' is not supported: nothing durable holds the request that made a video — no prompt, no negative prompt, by design (rule 7) — so there is nothing here to remix from. Send a new request with the prompt you want.","type":"invalid_request_error","param":null,"code":"not_supported"}}
```

A `RemixAsync` that always threw would read as "this client has not got to it yet", which is the
opposite of true — and rule 3 means anything published has to be kept for the life of `1.x`. So the
client teaches the refusal instead: `VideoErrorCodes.NotSupported`, the alternative named in the XML
doc, and both bodies in the test suite. This is now rule 10 in the repository's `CLAUDE.md`, because
it is not going to be the last time a hub refuses a route by design.

To enumerate, `ListJobsAsync` calls `GET /api/videos/jobs`. To "remix", send a new request with the
prompt you want.

### A clip is a `Video`, and a job row is a `MediaJob`

The hub runs both long modalities through **one** job registry and describes that record **two**
ways. `/v1/videos` answers OpenAI's object — `video_<32 hex>` id, a `status` word, an integer
`progress`, unix `created_at`/`completed_at`/`expires_at`. `/api/videos/jobs` answers phase 47's job
document — a bare GUID, a `state`, `step`/`totalSteps`. Neither is a rendering of the other: a
`MediaJob` built from a `video` object would have to guess `step`, and a `Video` built from a job row
would have to guess `progress`. So both types exist, and `VideoIdentifier.ToVideoId` / `ToJobId`
cross between the ids — because a caller who found a clip in the listing and now wants its bytes
holds the wrong spelling and would otherwise learn the prefix from a `404`.

### The watch is a poll, and it knows about 99

```csharp
await foreach (var progress in video.WatchAsync(clip.Id))
{
    Console.WriteLine($"{progress.Status} {progress.Progress}%");
}
```

1.3.0 refused a polling helper on the grounds that the hub streams. **It does not stream here**: the
Videos dialect has no events route, and a video id on `/api/images/jobs/{id}/events` is a `404`
because those routes are scoped to the image capabilities. So the loop exists, and writing it once
in the library is what carries the fact a caller cannot guess — **the hub caps `progress` at 99
until the render is over**, deliberately, so that a client stopping at 100 does not stop one round
trip before the bytes exist. `WatchAsync` ends on the terminal document, yields when the status or
the percentage changes, and takes a `VideoWatchOptions` to move the interval off its 2-second
default. Cancelling the token stops the watch, not the render.

### Read-once, again, and the `DELETE` that is not a cancel

```csharp
await using var content = await video.OpenContentAsync(clip.Id);   // no index: one clip per job
await using var file = File.Create("kite.mp4");
await content.Video.CopyToAsync(file);                              // ReadAllBytesAsync() on top
```

The read unlinks the bytes at the hub, so this request carries the same never-retry marker the image
content route got in 1.3.0: it is a `GET`, which is everything a transient-retry handler needs to
re-send it after a dropped connection and collect a `410` where the clip used to be. `410` is
`video_expired` — the bytes existed and are gone, read or evicted or past `expires_at` — which is a
different condition from the `404` that says there was never a clip, and this client surfaces the
code rather than flattening either into `null`.

`DeleteAsync` is OpenAI's `delete` and does **both halves**: it cancels the render *and* drops the
result. It is not `CancelJobAsync`'s bargain, where a job stopped at step 27 of 28 might still hand
you a picture.

### 16, not 8

`VideoSizes` ships because the grids differ: a video pipeline downsamples by 16 where an image
pipeline downsamples by 8. So `1920x1080` — a perfectly good picture, and the first size somebody
reusing their image code will send — is refused:

```json
{"error":{"message":"size '1920x1080' must have both sides a multiple of 16 — a video pipeline downsamples by 16 where an image pipeline downsamples by 8, and this is one of the two grids that differ","type":"invalid_request_error","param":"size","code":null}}
```

`Wide480` (`832x480`), `Portrait480`, `Square480`, `Wide720` and `Wide1088` — 1080p's honest
neighbour — plus `VideoSizes.IsValid` for a size you built yourself. The client does **not** refuse
a size locally: the recipe's own catalogue is narrower than the grid rule and only the node knows
it, so a local refusal would reject requests a node would have served.

The extension knobs travel as `X-InferHub-Video-*` headers — **not** the image ones, which this
route ignores entirely — gathered into one `VideoOptions` that formats every number with
`InvariantCulture`. A guidance of `5.5` sent as `5,5` from a Bulgarian or German machine is a `400`
naming the header, and it is a `400` that only reproduces on some developers' laptops.

## Dependencies, size, tests

- **Dependency budget unchanged**: `Microsoft.Extensions.Http` and
  `Microsoft.Extensions.DependencyInjection.Abstractions`. **Nothing here decodes a frame**, so no
  media library entered the budget — the same refusal images made.
- `<IsAotCompatible>` still true, zero trim/AOT warnings, Release build clean with `CS1591` as
  error, `dotnet format --verify-no-changes` clean.
- **197 tests per target framework (net9.0 and net10.0): 194 pass, 3 skip** — the skips are the
  env-gated integration suite, which runs only with `INFERHUB_TEST_BASEADDRESS` set. Skipped is not
  passed. 1.3.0 had 163 per TFM.
- New sample: `dotnet/samples/VideoClip` — list the queue, create, watch it climb, write the mp4,
  and print what served it.

## What this release does **not** establish, said out loud

- **No clip was ever rendered.** The hub available on the day — InferHub 3.37.0 on `:5080`, one node
  serving `chat` and `embed` with `tools.enabled=false` — provides no `video` capability. Nothing is
  claimed here about render times, what a five-second clip costs in megapixel-steps, what the mp4
  looks like, or how the read-once fetch behaves on tens of megabytes.
- **What *was* driven against that live hub — from the published package, not the working copy.**
  `dotnet add package InferHub.Client --version 1.4.0` into a clean directory pinned to nuget.org,
  then these checks against InferHub 3.37.0:

  | | result |
  |---|---|
  | `IInferHubVideoClient` from DI **and** from its public constructor | resolves; assembly `1.4.0.0` |
  | `ListJobsAsync` | `0 jobs, queued=0, active=0, retention=300s, persistence=none` |
  | `CreateAsync`, model the fleet holds | `503 api_error / capability_unavailable`, **`Retry-After: 30s`** |
  | `CreateAsync`, model nobody holds | `404 not_found_error / model_not_found`, `param=model`, **no retry** |
  | `CreateAsync` with `Size = "1920x1080"` | `400`, `param=size` — the 16-grid refusal, in full |
  | `CreateAsync` with `Size = VideoSizes.Wide1088` | past validation → `503 … 'video'` |
  | `CreateAsync` with `Seconds = 120` | `400`, `param=seconds` |
  | `VideoOptions { Steps = 200 }` | `400`, **`param=X-InferHub-Video-Steps`** |
  | `VideoOptions { Guidance = 5.5 }` under `bg-BG` | past validation → `503 … 'video'` |
  | `GetAsync(unknown)` | `null` |
  | `OpenContentAsync(unknown)` | `404 / video_not_found`, `param=id` |
  | `DeleteAsync(unknown)` | `404 / video_not_found`, `param=id` |
  | `WatchAsync(unknown)` | `404 / video_not_found` — the client's own, raised because the first poll found nothing |
  | `VideoIdentifier` round trip | `video_1111…` → `11111111-2222-…` → `video_1111…` |
  | **`images.ListJobsAsync`** (the 1.3 surface) | `0 jobs, retention=300s` |
  | **blocking chat** (the 1.0 surface) | *"Hello."*, `ServedBy: node` |

  Four rows carry the weight. The fifth and sixth are the **grid**, established against the real
  hub in both directions — `1920x1080` refused with the sentence, `VideoSizes.Wide1088` accepted and
  failing later, for want of a node. The eighth is the hub reading a header **this client wrote**, so
  `VideoOptions` is established end to end. And the ninth is the invariant-culture claim proved the
  only way it can be: under a Bulgarian thread culture, a `Guidance` of `5.5` reached routing rather
  than collecting the `400` the hub answers for `'5,5'`.

- **Every *refusal* in the new test suite is recorded from that live hub**, pasted verbatim with its
  escapes: both `501 not_supported` bodies, the `503 capability_unavailable` with its `Retry-After`,
  the `404 model_not_found`, the `404 video_not_found`, the 16-grid `400` beside the `1920x1080` one,
  the `seconds` ceiling `400`, and the two extension-header `400`s that name the header in
  `error.param`.
- **The success shapes are derived, not recorded, and each is marked as such in the test file** —
  the `video` object in its queued, in-progress, completed and failed states, its `expires_at`
  arithmetic, the deletion document, and a video row in the job listing. They come from the hub
  serializers that write those bytes (`VideoRenderer.Object`, `VideoRenderer.Progress`,
  `VideoRenderer.ExpiresAt`, `ImageJobView.Describe`) rather than from what a client author expects.
  Derived is weaker than recorded; phase 25 is where a real one arrives. The one success body that
  *was* recorded is the empty job listing.
- **The read-once path has never delivered or destroyed a real clip**, because there has never been
  one. The never-retry marker is asserted against the transport, not against the hub.
- **`WatchAsync` has never watched a real render.** Its loop is asserted against a scripted sequence
  of documents; the 99-cap it is built around is read from the hub's own `VideoRenderer.Progress`.
- **`conformance/cases.json` still does not exist** — phase 15. The four shapes this phase learned
  are written into `spec/README.md` under "the shapes that have actually broken clients", along with
  two corrections to the video rows that were wrong before this phase looked.
- Ingestion and search, the admin catch-up, and the node as a target remain phases 12–14.

## Install

```
dotnet add package InferHub.Client --version 1.4.0
```
