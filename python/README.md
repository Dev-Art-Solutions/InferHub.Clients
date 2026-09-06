# inferhub-client — the Python client

[![PyPI](https://img.shields.io/pypi/v/inferhub-client.svg)](https://pypi.org/project/inferhub-client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A small, typed Python client for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) — a
self-hosted, Ollama-compatible inference mesh. The **core** surface (chat, generate, embeddings,
model listing, status, health) shipped in `0.1.0`. `v0.2.0` adds **retrieval**: the vector
data-plane, the `X-InferHub-Retrieve*` RAG headers, ingestion and search. Modalities, admin and the
node land in `1.0.0` — see `plans/roadmap-polyglot-clients.md` for the shape of the rest of the
track.

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

## A node as a target

A solo InferHub node serves this same Ollama-dialect surface on its own address — pointing
`InferHubClient`/`AsyncInferHubClient` at a node's URL instead of a coordinator's is the whole of
"run it locally." `probe()` and the node-only routes (`/api/version`, the `/api/collections`
lifecycle) land in `1.0.0`, mirroring the C# client's phase 14 (`14 D7`).

## Development

```
pip install -e ".[test]"
pytest                              # 53 pass, 6 skipped (cases outside v0.2.0's surface, see below)
ruff check src tests examples
ruff format --check src tests examples
```

`tests/test_conformance.py` drives the shared corpus at `../conformance/cases.json` — the same file
the C# client's `ConformanceCorpusTests.cs` reads. A case whose `kind` this client does not cover
yet (the node, the OpenAI dialect) is skipped with a named reason rather than silently omitted.

## License

MIT — see [LICENSE](LICENSE).
