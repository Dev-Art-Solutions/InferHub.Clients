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
| `POST /api/videos/jobs` | ✓ | — | **hub only**; a node serves video through `/v1/videos` |
| `/v1/chat/completions`, `/v1/completions` | ✓ | ✓ | the OpenAI dialect |
| `/v1/embeddings`, `/v1/models`, `/v1/models/{id}` | ✓ | ✓ | |
| `POST /v1/audio/transcriptions` | ✓ | ✓ | |
| `POST /v1/audio/speech` | ✓ | ✓ | streams since hub 3.37.0 — see below |
| `POST /v1/images/generations\|edits\|variations` | ✓ | ✓ | |
| `POST /v1/videos`, `GET /v1/videos/{id}`, `/content`, `POST /{id}/remix` | ✓ | ✓ | `/content` is read-once |
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
| `X-InferHub-Audio-Sample-Rate` | on speech responses |
| `X-InferHub-Speech-Characters` | what was metered — characters, not tokens |

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
- **An absent count stays absent.** A zero constructed to fill a field is not a measurement, and the
  hub is careful about this in both directions.

## `payloads/`

Response bodies recorded from a real hub, used by the C# tests today and by every client's tests
from phase 15. **Nothing here is hand-written**: a payload somebody typed is a payload that agrees
with what its author believed.
