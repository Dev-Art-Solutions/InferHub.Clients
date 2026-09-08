# js/v0.1.0 — the core client

The first TypeScript release: `inferhub-client` on npm, covering the Ollama-dialect core surface —
chat, generate (blocking and streaming), embeddings, model listing, status and health. Retrieval
(vectors, RAG, ingestion, search) lands in `0.2.0`; modalities, admin and the node in `1.0.0`.

## New

- **`InferHubClient`** — one class, not a sync/async pair. `chat`/`generate`/`embed`/`embedLegacy`/
  `listModels`/`getStatus`/`ping` return `Promise<T>`; `chatStream`/`generateStream` return
  `AsyncIterable<T>`. TypeScript has one async calling convention, so there is no sync façade to
  keep in sync with an async one the way Python's two classes do.
- `fetch` and `ReadableStream` only — no `node-fetch`, no SSE library, **zero runtime dependencies**.
  A shared NDJSON line-buffering async generator (`_stream.ts`) backs both streaming methods; a
  mid-stream `{"error": …}` chunk raises `InferHubError` out of the iterator instead of hanging.
- Dual build: `dist/index.mjs` (ESM) + `dist/index.cjs` (CJS) + separate `.d.ts`/`.d.cts`, via
  `tsup`. Works from `import` and `require` alike — confirmed from a scratch scaffold outside the
  repo via `npm pack`.
- Every request type accepts `extra?: Record<string, unknown>` merged into the outgoing body; every
  response type carries `extra: Record<string, unknown>` for fields this version does not model —
  the TypeScript equivalent of the Python client's `.extra` dict and the C# client's
  `[JsonExtensionData]` bag.
- `servedBy` and `sourceIds` are read from response headers even though `v0.1.0` has no way yet to
  *request* retrieval — same header contract phase 16 (Python) already found worth reading early,
  and it lets this release pass the same cross-language conformance cases unmodified.

## Verified against a real, running InferHub coordinator

Not just the mocked test suite (`vitest`, `fetch` mocked, 29 pass / 9 named-skip): `chat`,
`chatStream`, `embed`, `listModels`, `getStatus` and `ping` all ran against a live 3.37.0
coordinator with one meshed node, and a real `404` (`model 'llama3' not found`) confirmed the error
mapping matches actual hub output byte for byte, mirroring `InferHubError` with `statusCode: 404`.

## Compatibility

First release — nothing to be additive against yet. `package.json`'s `dependencies` is `{}`.
`npm -w js test`: 29 pass, 9 skip (named: retrieval, the node, and the OpenAI dialect are all
outside this version's surface, not silently omitted). Node ≥ 18, and the same `fetch`/
`ReadableStream` surface any evergreen browser, Deno and Bun already provide.

See `js/README.md` for the full API table and `js/examples/` for runnable `basic-chat.ts`,
`streaming-chat.ts` and `embeddings.ts`.
