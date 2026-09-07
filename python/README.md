# inferhub-client — the Python client

[![PyPI](https://img.shields.io/pypi/v/inferhub-client.svg)](https://pypi.org/project/inferhub-client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A small, typed Python client for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) — a
self-hosted, Ollama-compatible inference mesh. The **core** surface (chat, generate, embeddings,
model listing, status, health) shipped in `0.1.0`. `v0.2.0` added **retrieval**: the vector
data-plane, the `X-InferHub-Retrieve*` RAG headers, ingestion and search. **`v1.0.0`** adds audio,
images, the admin plane and the node — the whole hub surface this client's design covers — and
starts this package's semver contract: from here, new capability is additive only (a new method, a
new optional field), never a changed signature.

**One dependency: `httpx`.** No pydantic — dataclasses do the job and every response type carries an
`extra` dict for fields this version does not know about yet, the same escape hatch the C# client's
`[JsonExtensionData]` gives it.

## Install

```
pip install inferhub-client
```

## Quick start

Sync:

```python
from inferhub_client import InferHubClient, ChatMessage, ChatRequest

with InferHubClient("http://localhost:5080/", api_key="sk-client-token-1") as client:
    answer = client.chat(ChatRequest(
        model="llama3",
        messages=[ChatMessage(role="user", content="Say hi in one word.")],
    ))
    print(answer.message.content)
```

Async — the same shapes, `await`ed:

```python
import asyncio
from inferhub_client import AsyncInferHubClient, ChatMessage, ChatRequest

async def main():
    async with AsyncInferHubClient("http://localhost:5080/", api_key="sk-client-token-1") as client:
        answer = await client.chat(ChatRequest(
            model="llama3",
            messages=[ChatMessage(role="user", content="Say hi in one word.")],
        ))
        print(answer.message.content)

asyncio.run(main())
```

`InferHubClient` and `AsyncInferHubClient` are **two thin façades over the same rules**
(`_base.py`'s header building, error mapping and NDJSON parsing) rather than one client with a
sync-over-`asyncio.run` shim — the latter breaks the moment a sync call happens inside code that is
already running an event loop, which is exactly where a web framework's request handler lives.

## API surface

| Method | Endpoint |
|---|---|
| `list_models()` | `GET /api/tags` |
| `chat(request, retrieval=None)` | `POST /api/chat` with `stream:false` |
| `chat_stream(request, retrieval=None)` | `POST /api/chat` with `stream:true` — an iterator/async iterator of `ChatResponse` |
| `generate(request, retrieval=None)` | `POST /api/generate` with `stream:false` |
| `generate_stream(request, retrieval=None)` | `POST /api/generate` with `stream:true` |
| `embed(request)` | `POST /api/embed` (batch — a string or a list of strings) |
| `embed_legacy(request)` | `POST /api/embeddings` (legacy single prompt) |
| `get_status()` | `GET /api/status` |
| `ping()` | `GET /health` — `True`/`False`, never raises for a non-success status |
| `upsert(collection, VectorUpsert)` | `POST /api/vector/{collection}/upsert` |
| `query(collection, VectorQuery)` | `POST /api/vector/{collection}/query` |
| `retrieve(collection, VectorQuery)` | `POST /api/vector/{collection}/retrieve` |
| `get_record(collection, id)` | `GET /api/vector/{collection}/{id}` — `None` on 404 |
| `delete_record(collection, id)` | `DELETE /api/vector/{collection}/{id}` — `bool` |
| `ingest_text(collection, TextDocument)` | `POST /api/collections/{collection}/documents` |
| `ingest_file(collection, FileDocument)` | same route, multipart |
| `list_documents(collection)` | `GET /api/collections/{collection}/documents` |
| `get_document(collection, id)` | `GET .../documents/{id}` — `None` on 404 |
| `get_chunks(collection, id)` | `GET .../documents/{id}/chunks` |
| `delete_document(collection, id)` | `DELETE .../documents/{id}` — `None` on 404 |
| `search(collection, query_or_request)` | `POST /api/collections/{collection}/search` |
| `transcribe(TranscriptionRequest)` | `POST /v1/audio/transcriptions` — always `verbose_json` |
| `transcribe_document(TranscriptionRequest)` | same route, `text`/`srt`/`vtt` returned unaltered |
| `create_speech(SpeechRequest)` | `POST /v1/audio/speech` — a live stream the caller closes |
| `stream_speech(SpeechRequest)` | same route, `stream_format` forced to `sse` — an iterator of `SpeechChunk` |
| `generate_image(ImageGenerationRequest)` | `POST /v1/images/generations` |
| `edit_image(ImageEditRequest)` | `POST /v1/images/edits`, multipart |
| `create_image_variation(ImageVariationRequest)` | `POST /v1/images/variations`, multipart |
| `submit_image_generation/edit/variation(...)` | `POST /api/images/jobs` — the async job seam |
| `list_image_jobs()` | `GET /api/images/jobs` — client-scoped |
| `get_image_job(id)` | `GET /api/images/jobs/{id}` — `None` on 404 |
| `watch_image_job(id)` | `GET /api/images/jobs/{id}/events` — an iterator of `MediaJob` |
| `open_image_content(id, index)` | `GET .../content/{index}` — read-once stream |
| `cancel_image_job(id)` | `DELETE /api/images/jobs/{id}` |
| `probe()` | `GET /api/status`, discriminated on `mode` |
| `get_node_version()` | `GET /api/version` — **node-only** |
| `list_node_collections()` / `get_node_collection` / `create_node_collection` / `drop_node_collection` | `/api/collections` — **node-only** |
| `list_nodes/cordon/uncordon/deregister` | `/api/admin/nodes/*` — **admin key** |
| `list_admin_collections/get_admin_collection/create_admin_collection/drop_admin_collection/rebuild_admin_collection` | `/api/admin/vector/collections/*` — **admin key** |
| `stream_admin_events()` | `GET /api/admin/stream` — an iterator of `AdminEvent` |
| `list_profiles/get_profile/put_profile/delete_profile/get_node_profile` | `/api/admin/profiles/*` |
| `pull_model/delete_model/warm_model/pull_tool_model/delete_tool_model` | `/api/admin/nodes/{id}/models/*` |
| `list_model_matrix()` / `ensure_model(model, replicas=None)` | `/api/admin/models*` |
| `query_usage(...)` / `list_clients()` | `/api/admin/usage`, `/api/admin/clients` |

## Audio (v1.0.0)

```python
from inferhub_client import SpeechRequest, TranscriptionRequest

with open("meeting.wav", "rb") as f:
    transcript = client.transcribe(TranscriptionRequest(model="whisper", audio=f, filename="meeting.wav"))
print(transcript.text, transcript.language, transcript.duration)

speech = client.create_speech(SpeechRequest(model="piper", input="Hello.", response_format="wav"))
with speech.response as response:
    open("out.wav", "wb").write(response.read())
```

`create_speech` and `stream_speech` are the same request either way (dotnet phase-9 D2) — with
`stream_format` unset the hub writes the whole file and `speech.response` is read once; with
`stream_speech` the same bytes arrive framed as SSE, one `SpeechChunk` per `speech.audio.delta`
plus a terminal `speech.audio.done` carrying `usage`/`characters` and no audio. **A `usage` of three
zeros on that terminal frame is a true count** for a phoneme model that tokenized nothing — the
number that reconciles with a bill is `characters`. Neither route takes retrieval or provider
headers: audio dispatches to whichever node declared the capability, no cloud provider is in the
path.

## Images (v1.0.0)

```python
from inferhub_client import ImageGenerationRequest, ImageOptions

picture = client.generate_image(
    ImageGenerationRequest(model="sdxl", prompt="a lighthouse in fog", options=ImageOptions(steps=28))
)
print(picture.data[0].b64_json[:32])

job = client.submit_image_generation(ImageGenerationRequest(model="sdxl", prompt="a lighthouse"))
for update in client.watch_image_job(job.id):
    print(update.state, update.step, "/", update.total_steps)
content = client.open_image_content(job.id, 0)   # read-once — a retry is a 410
```

The picture comes back base64 in the envelope: the hub stores nothing, so there is no URL to serve.
For a render that should outlive one HTTP connection, submit it as a job instead — `list_image_jobs`
is client-scoped (holding a job id is how a picture is fetched, so a fleet-wide listing would hand
other tenants' ids out) and lists *work*, not results: a job whose images were already delivered is
still there with nothing left to fetch.

## Video

There is no video module. The hub refuses `GET /v1/videos` and `POST /v1/videos/{id}/remix` with a
permanent `501 not_supported` — an id is itself the capability to fetch the bytes, and nothing
durable holds the prompt that made a clip — so this client does not publish a method that could only
throw (root rule 10). `VideoErrorCodes.NOT_SUPPORTED` names the code for a caller who wants to
recognise it; the fix is to send a new request with the prompt you want.

## Admin (v1.0.0)

```python
from inferhub_client import InferHubClient, NodeProfile

with InferHubClient(base_url, admin_key) as admin:   # an *admin* key, not a client key
    for node in admin.list_nodes():
        print(node.node_id)
    admin.put_profile("gpu-nodes", NodeProfile(selector={"labels": {"gpu": "true"}}, max_concurrency=4))
    for event in admin.stream_admin_events():         # snapshot + vector.* + model-progress frames
        print(event.event, event.data)
```

Admin methods live on the same `InferHubClient`/`AsyncInferHubClient` as everything else — there is
no second class to inject. Dotnet keeps admin on its own `IInferHubAdminClient` because a new member
on a *published* C# interface breaks every implementer holding a test double; a Python class has no
such concern, so a caller reaches the admin plane by constructing the client with an admin key
instead of a client key. Every admin failure is the plain `{"error": "..."}` envelope (never the
OpenAI one), so it surfaces as `InferHubError`, same as the core surface.

`stream_admin_events()` ends when the server closes the stream or the caller stops iterating; there
is no built-in reconnect-with-backoff — a caller who wants one wraps the generator in their own
retry loop, keeping the backoff policy a caller decision.

## Retrieval (v0.2.0)

```python
from inferhub_client import ChatRequest, ChatMessage, RetrievalOptions

answer = client.chat(
    ChatRequest(model="llama3", messages=[ChatMessage(role="user", content="What is InferHub?")]),
    retrieval=RetrievalOptions(collection="docs", k=5),
)
print(answer.message.content, answer.source_ids)
```

`retrieval` is a call-scoped keyword on `chat`/`generate`, not a field on the request body — it sets
`X-InferHub-Retrieve*`/`X-InferHub-Rerank` for that call only. Retrieval asked for and unavailable is
HTTP 424, raised as `InferHubRetrievalException` (a subclass of `InferHubError`, so catching the base
type still works) — a different condition from a missing model (404).

## Ingestion and search

```python
from inferhub_client import TextDocument

client.ingest_text("docs", TextDocument(id="policy", text="Payroll runs on the fifth working day."))
results = client.search("docs", "when does payroll run?")
for hit in results.hits:            # kept in the hub's own wire order — never re-sorted by score
    print(hit.document_id, hit.score, hit.text)
```

An ingest that lands **partially** answers HTTP 500 with a real body (`documentId`, `chunks`,
`chunksEmbedded`, `error`) — `ingest_text`/`ingest_file` return an `IngestResult` for this case
rather than raising, so the document id and the chunks that did land are not thrown away. A genuine
server error (no `documentId`/`status` in the body) still raises `InferHubError` as normal.

## Streaming

```python
for chunk in client.chat_stream(ChatRequest(model="llama3", messages=[...])):
    print(chunk.message.content, end="", flush=True)
```

A terminal error chunk (`{"error": "...", "done": true}`) raises `InferHubError` out of the loop
instead of the iterator hanging or ending quietly with a partial answer nobody was told about.

## Errors

Every non-success response raises `InferHubError(status_code, message, response_body,
retry_after=...)`. `retry_after` is populated from `Retry-After` when the hub sends one — the
refusals that carry it are the ones worth retrying rather than only reporting.

```python
from inferhub_client import InferHubError

try:
    client.embed(EmbedRequest.from_text("nomic-embed-text", "hello"))
except InferHubError as e:
    print(e.status_code, e.message, e.retry_after)
```

## `extra`: the fields this version does not know about yet

`ChatRequest`/`GenerateRequest.extra` merges straight into the request body (Ollama's `options`,
`format`, `keep_alive`, tool definitions — anything the hub accepts that this client has not typed);
every response dataclass keeps unrecognized fields in its own `.extra` dict on the way back. Typing
every Ollama option was considered and rejected, same as the C# client: the hub owns that schema and
grows it independently of this package's release cadence.

## A node as a target (v1.0.0)

A solo InferHub node serves this same Ollama-dialect surface on its own address — pointing
`InferHubClient`/`AsyncInferHubClient` at a node's URL instead of a coordinator's is the whole of
"run it locally," mirroring the C# client's phase 14 (`14 D7`, root rule 6).

```python
probe = client.probe()   # one GET /api/status, discriminated on whether `mode` is present
if probe.kind == "solo_node":
    print(probe.node_status.name, probe.node_status.retrieval.rerank)   # "none" | "llm", a string
    for collection in client.list_node_collections():
        print(collection.name, collection.dimension)
else:
    print(len(probe.hub_status.nodes), "nodes on the fleet")
```

`probe.kind` is `"hub"` or `"solo_node"` — the hub's `/api/status` document never carries a `mode`
field at all; a node's always does. The five node-only methods
(`get_node_version`/`list_node_collections`/`get_node_collection`/`create_node_collection`/
`drop_node_collection`) are named so a `404` against the wrong target reads as a name, not a
surprise: `list_node_collections` (`/api/collections`, a client key) is a different route, auth and
shape from `list_admin_collections` (`/api/admin/vector/collections`, an admin key, with replica
placement) — never the same method for two different things.

A solo embed against a vendor-typed backend with no embeddings API (an Anthropic-backed node, say)
is a permanent `501`; a capability an operator disabled is a `503` with `Retry-After` — two
different refusals, both already surfaced through `InferHubError.status_code`/`.retry_after`.

## Development

```
pip install -e ".[test]"
pytest                              # 98 pass, 3 skipped (the OpenAI chat dialect — see below)
ruff check src tests examples
ruff format --check src tests examples
```

`tests/test_conformance.py` drives the shared corpus at `../conformance/cases.json` — the same file
the C# client's `ConformanceCorpusTests.cs` reads. The 3 remaining skips are `/v1/chat/completions`
cases: the OpenAI chat dialect is dotnet-only (roadmap D3 scopes this track to the Ollama dialect,
retrieval, modalities, admin and the node) and is not a gap in this client's coverage of its own
surface.

## License

MIT — see [LICENSE](LICENSE).
