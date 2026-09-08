# inferhub-client — the TypeScript client

[![npm](https://img.shields.io/npm/v/inferhub-client.svg)](https://www.npmjs.com/package/inferhub-client)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A small, typed TypeScript client for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) — a
self-hosted, Ollama-compatible inference mesh. The **core** surface (chat, generate, streaming,
embeddings, model listing, status, health) ships in `0.1.0`. `v0.2.0` adds **retrieval**: the
vector data-plane, the `X-InferHub-Retrieve*` RAG headers, ingestion and search. `v1.0.0` adds
audio, images, the admin plane and the node.

**Zero runtime dependencies.** `fetch` and `ReadableStream` are the platform — present natively in
Node ≥ 18, every evergreen browser, Deno and Bun. One package, two build targets (ESM primary, CJS
secondary via [`tsup`](https://tsup.egoist.dev/), a dev-only build tool that never ships in
`dependencies`).

## Install

```
npm install inferhub-client
```

## Quick start

```ts
import { InferHubClient } from "inferhub-client";

const client = new InferHubClient({ baseUrl: "http://localhost:5080/", apiKey: "sk-client-token-1" });

const answer = await client.chat({
  model: "llama3",
  messages: [{ role: "user", content: "Say hi in one word." }],
});
console.log(answer.message?.content);
```

One class, no sync/async split: everything async is a `Promise`, and a stream is an
`AsyncIterable` — TypeScript's native idiom, not a second façade to keep in sync (unlike the
Python client, which needs two classes because Python has two incompatible calling conventions).

## API surface (v0.1.0)

| Method | Endpoint |
|---|---|
| `listModels()` | `GET /api/tags` |
| `chat(request)` | `POST /api/chat` with `stream:false` → `Promise<ChatResponse>` |
| `chatStream(request)` | `POST /api/chat` with `stream:true` → `AsyncIterable<ChatResponse>` |
| `generate(request)` | `POST /api/generate` with `stream:false` → `Promise<GenerateResponse>` |
| `generateStream(request)` | `POST /api/generate` with `stream:true` → `AsyncIterable<GenerateResponse>` |
| `embed(request)` | `POST /api/embed` (batch — a string or a list of strings) |
| `embedLegacy(request)` | `POST /api/embeddings` (legacy single prompt) |
| `getStatus()` | `GET /api/status` |
| `ping()` | `GET /health` — `boolean`, never throws for a non-success status |

Retrieval, ingestion, search, audio, images, admin and the node are **not in this version** — see
`v0.2.0`/`v1.0.0` in the [root README](../README.md)'s parity table. `probe()` and the OpenAI
dialect (`/v1/*`) are also later phases; there is no method here that could only throw.

## Streaming

```ts
for await (const chunk of client.chatStream({
  model: "llama3",
  messages: [{ role: "user", content: "Count to five." }],
})) {
  process.stdout.write(chunk.message?.content ?? "");
}
```

The hub's streaming responses are newline-delimited JSON (NDJSON), never actual
`text/event-stream` — `chatStream`/`generateStream` decode and split a `ReadableStream<Uint8Array>`
on `\n` through one shared line-buffering generator, mapping each line through `JSON.parse`. A
terminal error chunk (`{"error": "...", "done": true}`) throws `InferHubError` out of the loop
instead of the iterator hanging or ending quietly with a partial answer nobody was told about.

## Errors

Every non-success response throws. Which envelope arrived decides the exception type, never which
method was called: `/api/*` answers `{"error":"..."}` and throws the base `InferHubError`; `/v1/*`
and routes that reuse its shape answer `{"error":{"message":...,"type":...,"param":...,"code":...}}`
and throw `InferHubOpenAiException` (`errorCode`/`param` intact). A `424` — retrieval asked for and
unavailable — always throws `InferHubRetrievalException`, a subclass of `InferHubError`, so a
`catch` for the base type still matches.

```ts
import { InferHubError } from "inferhub-client";

try {
  await client.embed({ model: "nomic-embed-text", input: "hello" });
} catch (error) {
  if (error instanceof InferHubError) {
    console.log(error.statusCode, error.message, error.retryAfter);
  }
}
```

`retryAfter` (seconds) is populated from a `Retry-After` header when the hub sends one — the
refusals that carry it are the ones worth retrying rather than only reporting.

## `extra`: the fields this version does not know about yet

Every request type accepts `extra?: Record<string, unknown>`, merged straight into the request
body (Ollama's `options`, `format`, `keep_alive`, tool definitions — anything the hub accepts that
this client has not typed); every response type carries an `extra: Record<string, unknown>` built
from whatever fields were not consumed. Typing every Ollama option was considered and rejected,
same as the C# and Python clients: the hub owns that schema and grows it independently of this
package's release cadence.

```ts
const answer = await client.chat({
  model: "llama3",
  messages: [{ role: "user", content: "hi" }],
  extra: { tools: [{ type: "function", function: { name: "get_weather" } }] },
});
```

## `X-InferHub-Served-By` and `X-InferHub-Sources`

Every `ChatResponse`/`GenerateResponse` carries `servedBy` (which node or `provider:<id>`
answered) and `sourceIds` (retrieval source document ids), read from response headers — surfaced,
never interpreted. This client does not route, retry elsewhere, or prefer on `servedBy`: deciding
to re-send a prompt to a second address is a second disclosure of the same prompt.
`X-InferHub-Sources` is parsed even though `v0.1.0` has no way yet to opt into retrieval, and both
shapes a real hub has sent — a JSON array and a comma-separated string — are handled.

## Node, browser, Deno, Bun

No platform branches in the source: `fetch`, `ReadableStream` and `TextDecoder` are all native in
every target runtime named above. Pass a `fetch` override in the constructor for tests or a
runtime-specific implementation:

```ts
const client = new InferHubClient({ baseUrl, fetch: myFetch });
```

## Development

```
npm install
npm run build         # tsup — dist/index.mjs + dist/index.cjs + .d.ts
npm run typecheck      # tsc --noEmit
npm test                # vitest run
```

`test/conformance.test.ts` drives the shared corpus at `../conformance/cases.json` — the same file
the C#, Python and (later) Go runners read. Cases whose `kind` is outside `v0.1.0`'s surface
(`probe`, the OpenAI dialect, retrieval/ingestion/search/chunks) are skipped by name, not filtered
out of the file — 4 cases covered, 9 skipped, all 13 accounted for.

## License

MIT — see [LICENSE](LICENSE).
