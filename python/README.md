# python/ — the Python client

**Empty until phase 16.** The folder is a placeholder and contains **no manifest** on purpose: an
empty `pyproject.toml` is a package that resolves and does nothing, which is worse than an absent
one.

## What lands here

- **Phase 16** (`python/v0.1.0`) — the core client: chat, generate, streaming, embeddings, models,
  status, auth, the error model.
- **Phase 17** (`python/v0.2.0`) — the vector data plane, RAG headers, ingestion, search.
- **Phase 18** (`python/v1.0.0`) — audio, images, video, admin, a node as a target, and 1.0.

`async` with a sync façade, typed, **`httpx` and nothing else** — `pydantic` is deliberately not
taken: dataclasses and `TypedDict` do the job, and pydantic v1/v2 is somebody else's dependency war
inherited by every consumer. Examples in `python/examples/`, run by CI against the conformance stub.
