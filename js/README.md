# js/ — the TypeScript client

**Empty until phase 19.** The folder is a placeholder and contains **no manifest** on purpose: an
empty `package.json` is a package that resolves and does nothing, which is worse than an absent one.

## What lands here

- **Phase 19** (`js/v0.1.0`) — the core client: chat, generate, streaming, embeddings, models,
  status, auth, the error model. Node, browser, Deno and Bun; ESM with a CJS build.
- **Phase 20** (`js/v0.2.0`) — the vector data plane, RAG headers, ingestion, search.
- **Phase 21** (`js/v1.0.0`) — audio, images, video, admin, a node as a target, and 1.0.

`fetch` and `ReadableStream`, **zero runtime dependencies**. Examples in `js/examples/`, run by CI
against the conformance stub.
