# spec/ — the hub surface these clients implement

**What a client may call, and what a *node* also answers.** Written from the hub's source, not from
any client, and kept here rather than in one language's test project so a second language does not
have to reshape somebody's xUnit fixtures to read it.

> Recorded against InferHub **`3.37.0`**. The hub is the authority; when this file and the hub
> disagree, the hub is right and this file is a bug.

Phase 15 turns this folder into an executable conformance corpus (`conformance/cases.json` plus a
thin runner per language). Until then it is documentation with one useful property: **it was copied
from a real hub rather than from a client**, which is the difference between a corpus that tests
something and one that agrees with whatever it was derived from.

## Two targets, one surface

A **coordinator** dispatches to a fleet. A **node in solo mode** serves its own API on its own
address. They speak the same paths with the same bodies, which is why a client is a base address and
not two client types.

Legend: **✓** served · **—** not served · *(auth)* which key opens it.

| Path | Hub | Node | Notes |
|---|---|---|---|
| `GET /health` | ✓ | ✓ | open by design, so monitoring can poll it |
| `GET /api/version` | — | ✓ | **node only** |
| `GET /api/status` | ✓ | ✓ | hub's carries the fleet, the vector block and the cloud-provider rows; the node's describes itself. `401` without an admin key on a containerised hub |
| `GET /api/tags` | ✓ | ✓ | Ollama-shaped model list |
| `GET /api/nodes` | ✓ | — | **hub only** — the fleet view |
| `POST /api/generate` | ✓ | ✓ | NDJSON when `stream:true` |
| `POST /api/chat` | ✓ | ✓ | NDJSON when `stream:true` |
| `POST /api/embed` | ✓ | ✓ | batch: `input` is a string or an array |
| `POST /api/embeddings` | ✓ | ✓ | legacy single `prompt` |
| `POST /api/vector/{c}/upsert\|query\|retrieve` | ✓ | ✓ | |
| `GET\|DELETE /api/vector/{c}/{id}` | ✓ | ✓ | `404` is a real answer, not an error |
| `GET /api/collections` | — | ✓ | **node only** — the solo corpus it owns |
| `POST /api/collections/{c}/documents` | ✓ | ✓ | multipart; streams through the hub rather than buffering |
| `GET /api/collections/{c}/documents/{id}/chunks` | ✓ | ✓ | |
| `POST /api/collections/{c}/search` | ✓ | ✓ | reranking via `X-InferHub-Rerank` |
| `POST /api/tools/{capability}` | ✓ | ✓ | the capability seam — STT, TTS and the rest |
| `POST /api/images/jobs` | ✓ | ✓ | async: `queued → running → succeeded\|failed\|cancelled` |
| `GET /api/images/jobs/{id}` | ✓ | ✓ | |
| `GET /api/images/jobs/{id}/events` | ✓ | ✓ | SSE progress |
| `GET /api/images/jobs/{id}/content/{index}` | ✓ | ✓ | **read-once — the read unlinks the bytes** |
| `GET /api/videos/jobs` | ✓ | — | **hub only** — the client-scoped listing, in the *job* vocabulary. There is no `POST` here: a clip is submitted through `/v1/videos`. A node keeps no index to enumerate |
| `/v1/chat/completions`, `/v1/completions` | ✓ | ✓ | the OpenAI dialect |
| `/v1/embeddings`, `/v1/models`, `/v1/models/{id}` | ✓ | ✓ | |
| `POST /v1/audio/transcriptions` | ✓ | ✓ | |
| `POST /v1/audio/speech` | ✓ | ✓ | streams since hub 3.37.0 — see below |
| `POST /v1/images/generations\|edits\|variations` | ✓ | ✓ | |
| `POST /v1/videos`, `GET /v1/videos/{id}`, `/content`, `DELETE /v1/videos/{id}` | ✓ | ✓ | OpenAI's Videos API. `/content` is read-once and takes **no index** — one clip per job. `DELETE` cancels *and* drops |
| `GET /v1/videos`, `POST /v1/videos/{id}/remix` | **501** | **501** | mapped so the refusal is a sentence rather than a `404` a client reads as "old hub". Listing: a video id is itself the capability to fetch the bytes. Remix: nothing durable holds the prompt that made a clip |
| `/api/admin/**` | ✓ *(admin)* | — | **hub only** — fleet, profiles, model lifecycle, collections, usage, clients, the SSE stream |
| `GET /metrics` | ✓ *(admin)* | — | open only when `Metrics:OpenScrape=true` |
| `GET /console` | ✓ | — | the management UI |

**Three token sets, independently:** `Auth:ApiKeys` / `Auth:Clients` for inference,
`Auth:AdminApiKeys` for `/api/admin/**` and `/metrics`, and a node enrollment secret that no client
ever sees. Loopback skips auth unless `Auth:RequireAuthForLoopback=true`.

## Request headers a client may send

| Header | On | Meaning |
|---|---|---|
| `X-InferHub-Conversation` | chat, generate | opaque id; sticky routing. **Carries no content** |
| `X-InferHub-Retrieve` | chat, generate | opt into RAG against a collection |
| `X-InferHub-Retrieve-K`, `-Model`, `-Mode` | chat, generate | how much, embedded by what, and which retrieval mode |
| `X-InferHub-Rerank` | search | rerank the matches |
| `X-InferHub-Provider` | chat, generate, `/v1/*` | steer this request to a named cloud provider. An unknown id is **refused and counted**, never silently ignored |
| `X-InferHub-Image-*`, `X-InferHub-Video-*` | image, video | seed, steps, guidance, strength, projection, seam repair |
| `X-InferHub-Mask-Convention` | image edits | which way round the mask reads |

## Response headers a client must surface

| Header | Meaning |
|---|---|
| `X-InferHub-Served-By` | which node or `provider:<id>` answered. **Surfaced, never interpreted** — a client does not route on it |
| `X-InferHub-Sources` | retrieved ids. **A JSON array, but a real hub has also sent it comma-separated** — parse both. This is the corpus's first case |
| `X-InferHub-Audio-Sample-Rate` | on **streamed** speech responses only — measured off the worker's own first chunk, and for `pcm` the only place the rate exists |
| `X-InferHub-Speech-Characters` | what was metered — characters, not tokens. **Streamed responses only**, same as above |

## The shapes that have actually broken clients

Each of these is a conformance case in phase 15, and each is here because it is not derivable from
a schema.

- **A mid-stream error terminates the stream.** NDJSON inference ends with `{"error":…,"done":true}`
  rather than a transport failure. A client that only checks `done` hangs; a client that only checks
  HTTP status reports success.
- **`424 Failed Dependency` is not `404`.** Retrieval was asked for and is unavailable — a different
  condition from a missing model, and it needs its own exception type.
- **`503` naming a capability** is what a solo embed against a vendor-typed node returns. Not a
  timeout, not a 404.
- **Read-once content.** `/content` unlinks as it reads. Retrying a failed download gets nothing,
  and a client that helpfully retries destroys the only copy.
- **`X-InferHub-Sources` in two shapes**, above.
- **`usage` of three zeros on `speech.audio.done` is a true count, not a placeholder** — a phoneme
  model tokenized nothing. A client that treats zero as "missing" reports the wrong thing.
- **On a multipart upload every form field must precede the file part.** Above the hub's
  `Tools:MaxStreamedBytes` the request is routed from the leading fields while the bytes are still
  arriving, so a field after the file is a `400` naming the field. The buffered path below that
  ceiling tolerates any order, **which is what makes this dangerous**: a client that writes the
  file first is correct on every test recording and wrong on the first real one, in production.
  Recorded from 3.37.0's `StreamedUpload`.
- **`error.param` names the field the hub blames, and the two audio routes blame different ones.**
  The same class of refusal — an unsupported `response_format` — comes back with `param: "model"`
  from `/v1/audio/transcriptions` and `param: "input"` from `/v1/audio/speech`. A client that maps
  `param` onto its own property names points the caller at the wrong field; surface it verbatim.
  Both recorded from 3.37.0.
- **`503` + `capability_unavailable` is not `404`.** "The fleet holds this model but no node is
  currently doing this kind of work" carries `Retry-After` and is worth retrying later; "no node
  holds the model" is a `404` with `code: "model_not_found"` and is not. Recorded from 3.37.0 for
  both `transcribe` and `speak`.
- **An absent count stays absent.** A zero constructed to fill a field is not a measurement, and the
  hub is careful about this in both directions.
- **The two dialects fail in two envelopes.** `/api/*` answers `{"error":"…"}`; `/v1/*` answers
  `{"error":{"message":…,"type":…,"param":…,"code":…}}`, because an OpenAI SDK reads
  `error.message` to build the exception it raises. A client that only knows the first surfaces the
  whole JSON body as its message — recorded from 3.37.0, both shapes, for the *same* refused steer.
- **`error.code` is a string or a number.** This project writes `"model_not_found"`; an upstream
  passed through writes `429`. The hub reads both (62) and so must a client.
- **`code` can be `null` on a real error.** The refused-steer `400` carries `param: "model"` and
  `code: null` — a client that treats a missing code as "not an OpenAI envelope" loses the message.
- **A streamed `/v1` frame with an empty `choices` array is the usage frame**, and it is the only
  place a streamed call reports token counts (`stream_options.include_usage`). A client that skips
  frames with no choices reports "usage: not available" for every stream.
- **An image job's SSE stream has no `[DONE]` and no sentinel of any kind.** Each frame is
  `event: <state>` plus the **whole job document**, and the stream simply ends after the terminal
  one. So a client keys on the payload's own `state` and stops there; one that waited for a sentinel
  hangs until the hub closes the socket, and one that treated the 15-second keep-alive as progress
  reports the same step twice. The keep-alive re-sends the current state rather than a comment,
  which is deliberate — a client that reconnected mid-render needs no catch-up `GET`.
- **A refused image extension header names the *header* in `error.param`**, not a body field:
  `param: "X-InferHub-Image-Steps"`, `"X-InferHub-Image-Strength"`,
  `"X-InferHub-Mask-Convention"`. Surface it verbatim. Recorded from 3.37.0, all three.
- **`param` is a header and `code` is `null` on the same error.** The extension-header refusals carry
  both at once, so a client that requires a `code` to recognise an OpenAI envelope loses the only
  sentence explaining what is wrong.
- **A variation's two refusals are the argument for a separate request type.** `prompt` on a
  variation is a `400` naming `prompt`, `mask` is a `400` naming `mask`, and both say which route to
  use instead. A client that models edits and variations as one record with nullable fields ships
  both refusals as runtime surprises. Recorded from 3.37.0.
- **A multipart image job must name its `operation`**; the hub refuses to guess, because a typo in a
  field name would otherwise turn a variation into an edit. The synchronous `/v1` routes take no
  `operation` at all — there the route *is* the operation.
- **A video size is a multiple of 16 where an image size is a multiple of 8.** `1920x1080` is a
  perfectly good image size and a `400` for a video, because every latent *video* pipeline in the
  hub's pinned wheel downsamples by 16 — so the first size a client author copies from their image
  code is the one that fails. The nearest size that passes is `1920x1088`. Recorded from 3.37.0,
  both ways round.
- **A `501` here is a decision, not an old hub.** `GET /v1/videos` and `POST /v1/videos/{id}/remix`
  are mapped so the refusal carries its reason. A client that ships those methods anyway publishes
  something that can only throw; a client that treats the `501` as "unsupported by this version"
  tells its caller to upgrade the hub, which will not help.
- **Video has no SSE, and its `progress` is capped at 99.** The image job seam streams
  (`/api/images/jobs/{id}/events`); the Videos dialect has no events route at all, and a video id on
  the images one is a `404` — those routes are scoped to the image capabilities. So a client polls
  `GET /v1/videos/{id}`, and it must key on the *status* rather than on `progress == 100`: the hub
  only ever writes 100 once the render is over, precisely so that a client stopping at 100 does not
  stop one round trip before the bytes exist.
- **One record, two id spellings.** `/v1/videos` says `video_<32 hex>` and `GET /api/videos/jobs`
  says the bare GUID, for the same job. A caller crossing between the listing and the bytes has to
  convert, and a client that does not offer the conversion teaches it by 404.
- **The job document is the same for images and video.** `/api/videos/jobs` renders through the same
  serializer, distinguished by `capability`, and each output's `url` already points at its own
  content route (`/v1/videos/{id}/content` for a clip). A client type named for one modality is a
  rename waiting to happen.
- **`data: [DONE]` is not JSON.** It ends the stream; deserializing it throws. And a stream that
  ends *without* it is a node that dropped: the hub already sent a terminal frame with
  `finish_reason: "stop"`, so the honest client keeps the partial answer rather than raising.

## `payloads/`

Response bodies recorded from a real hub, used by the C# tests today and by every client's tests
from phase 15. **Nothing here is hand-written**: a payload somebody typed is a payload that agrees
with what its author believed.
