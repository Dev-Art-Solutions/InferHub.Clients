# python/v0.1.0 — the core client

The first Python release: `inferhub-client` on PyPI, covering the Ollama-dialect core surface —
chat, generate (blocking and streaming), embeddings, model listing, status and health. Retrieval
(vectors, RAG, ingestion, search) lands in `0.2.0`; modalities, admin and the node in `1.0.0`.

## New

- **`InferHubClient`** (sync, `httpx.Client`) and **`AsyncInferHubClient`** (async,
  `httpx.AsyncClient`) — two thin façades over shared, I/O-free plumbing (`_base.py`: header
  building, error mapping, NDJSON parsing), not a sync-over-`asyncio.run` shim, which breaks the
  moment a sync call happens inside code already running an event loop.
- `chat`/`chat_stream`, `generate`/`generate_stream`, `embed`/`embed_legacy`, `list_models`,
  `get_status`, `ping`. A terminal NDJSON error chunk raises `InferHubError` out of the stream
  instead of hanging or ending quietly with a partial answer.
- Dataclasses throughout, no `pydantic`. Every response type carries an `.extra` dict for fields
  this version does not know about yet — the Python equivalent of the C# client's
  `[JsonExtensionData]` bag — and `ChatRequest`/`GenerateRequest.extra` merges straight into the
  outgoing body, so any Ollama option (`options`, `format`, `keep_alive`, tool definitions) is
  reachable without this client typing each one.
- `served_by` and `source_ids` are read from response headers even though `v0.1.0` has no way yet
  to *request* retrieval — the header contract is part of the core response either way, and it let
  this release consume 4 of the shared conformance corpus's 13 cases unmodified (the mid-stream
  terminal error, `424` vs `404`, and both `X-InferHub-Sources` shapes) — the first real number
  behind roadmap-polyglot-clients D8's claim that the corpus makes a second language cheap.

## Verified against a real, running InferHub coordinator

Not just the mocked test suite (`httpx.MockTransport`, 31 pass / 9 named-skip): `chat`,
`chat_stream`, `embed`, `list_models`, `get_status` and `ping` all ran against a live 3.37.0
coordinator with one meshed node, both sync and async clients, and a real `404`
(`model 'no-such-model-xyz' not found`) confirmed the error-message parsing matches actual hub
output byte for byte.

**Then verified again, for real, after the tag**: `pip install inferhub-client` from the public
PyPI index into a clean virtualenv, then `list_models()` (29 models) and `chat()` against the same
live coordinator from that installed package — not an editable install, not a built wheel installed
by hand, the actual thing anyone running `pip install inferhub-client` today gets.

## CI note, said out loud rather than left implicit

Two follow-up commits were needed before the tag would go green: `ruff` 0.16 turned on the `I`
(isort) and `RUF022` (`__all__` sort order) lint rules by default where `0.15` (the version this was
developed against) didn't, and the unpinned `ruff>=0.6` in `pyproject.toml`'s `test` extra picked up
0.16.6 in CI. Fixed by pinning `ruff==0.15.13` exactly — a linter whose pass/fail depends on which
release happened to resolve that day is not a check.

## Compatibility

First release — nothing to be additive against yet. `httpx` is the only runtime dependency.
`pytest python/tests`: 31 pass, 9 skip (named: retrieval, the node, and the OpenAI dialect are all
outside this version's surface, not silently omitted).

See `python/README.md` for the full API table and `python/examples/` for runnable
`basic_chat.py`, `streaming_chat.py` (sync + async) and `embeddings.py`.
