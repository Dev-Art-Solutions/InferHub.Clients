# inferhub-client — the Python client

[![PyPI](https://img.shields.io/pypi/v/inferhub-client.svg)](https://pypi.org/project/inferhub-client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A small, typed Python client for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) — a
self-hosted, Ollama-compatible inference mesh. `v0.1.0` is the **core** surface: chat, generate
(blocking and streaming), embeddings, model listing, status and health. Retrieval (vectors, RAG,
ingestion, search) lands in `0.2.0`; modalities, admin and the node in `1.0.0` — see
`plans/roadmap-polyglot-clients.md` for the shape of the rest of the track.

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

## API surface (v0.1.0)

| Method | Endpoint |
|---|---|
| `list_models()` | `GET /api/tags` |
| `chat(request)` | `POST /api/chat` with `stream:false` |
| `chat_stream(request)` | `POST /api/chat` with `stream:true` — an iterator/async iterator of `ChatResponse` |
| `generate(request)` | `POST /api/generate` with `stream:false` |
| `generate_stream(request)` | `POST /api/generate` with `stream:true` |
| `embed(request)` | `POST /api/embed` (batch — a string or a list of strings) |
| `embed_legacy(request)` | `POST /api/embeddings` (legacy single prompt) |
| `get_status()` | `GET /api/status` |
| `ping()` | `GET /health` — `True`/`False`, never raises for a non-success status |

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
pytest                              # 31 pass, 9 skipped (cases outside v0.1.0's surface, see below)
ruff check src tests examples
ruff format --check src tests examples
```

`tests/test_conformance.py` drives the shared corpus at `../conformance/cases.json` — the same file
the C# client's `ConformanceCorpusTests.cs` reads. A case whose `kind` this client does not cover
yet (retrieval, the node, the OpenAI dialect) is skipped with a named reason rather than silently
omitted; four cases (the mid-stream terminal error, `424` vs `404`, both `X-InferHub-Sources`
shapes) pass today, unmodified, because the corpus already knew the answer.

## License

MIT — see [LICENSE](LICENSE).
