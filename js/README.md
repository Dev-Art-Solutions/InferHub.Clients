# inferhub-client — the TypeScript client

[![npm](https://img.shields.io/npm/v/inferhub-client.svg)](https://www.npmjs.com/package/inferhub-client)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A small, typed TypeScript client for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) — a
self-hosted, Ollama-compatible inference mesh. The **core** surface (chat, generate, streaming,
embeddings, model listing, status, health) shipped in `0.1.0`. **`v0.2.0` adds retrieval**: the
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

## API surface (v0.2.0)

| Method | Endpoint |
|---|---|
| `listModels()` | `GET /api/tags` |
| `chat(request, retrieval?)` | `POST /api/chat` with `stream:false` → `Promise<ChatResponse>` |
| `chatStream(request, retrieval?)` | `POST /api/chat` with `stream:true` → `AsyncIterable<ChatResponse>` |
| `generate(request, retrieval?)` | `POST /api/generate` with `stream:false` → `Promise<GenerateResponse>` |
| `generateStream(request, retrieval?)` | `POST /api/generate` with `stream:true` → `AsyncIterable<GenerateResponse>` |
| `embed(request)` | `POST /api/embed` (batch — a string or a list of strings) |
| `embedLegacy(request)` | `POST /api/embeddings` (legacy single prompt) |
| `getStatus()` | `GET /api/status` |
| `ping()` | `GET /health` — `boolean`, never throws for a non-success status |
| `upsert(collection, upsert)` | `POST /api/vector/{collection}/upsert` |
| `query(collection, query)` / `retrieve(collection, query)` | `POST /api/vector/{collection}/query` \| `/retrieve` |
| `getRecord(collection, id)` | `GET /api/vector/{collection}/{id}` — `undefined` on 404 |
| `deleteRecord(collection, id)` | `DELETE /api/vector/{collection}/{id}` — `boolean` |
| `ingestText(collection, document)` / `ingestFile(collection, document)` | `POST /api/collections/{collection}/documents` |
| `listDocuments(collection)` | `GET /api/collections/{collection}/documents` |
| `getDocument(collection, id)` / `deleteDocument(collection, id)` | `.../{id}` — `undefined` on 404 |
| `getChunks(collection, id)` | `GET .../{id}/chunks` |
| `search(collection, query)` | `POST /api/collections/{collection}/search` — throws on a missing collection |

Audio, images, admin and the node are **not in this version** — see `v1.0.0` in the
[root README](../README.md)'s parity table. `probe()` and the OpenAI dialect (`/v1/*`) are also
later phases; there is no method here that could only throw.

## Retrieval

`RetrievalOptions` is a second, optional argument to `chat`/`chatStream`/`generate`/
`generateStream` — never a body field, since it applies to two different request shapes and is a
per-call concern, not part of either one:

```ts
const answer = await client.chat(
  { model: "llama3", messages: [{ role: "user", content: "What is InferHub?" }] },
  { collection: "docs", k: 5, rerank: true },
);
console.log(answer.message?.content, answer.sourceIds);
```

## Vector data-plane

```ts
await client.upsert("docs", { id: "a", text: "Payroll runs on the fifth working day." });
const matches = await client.query("docs", { text: "when is payroll", topK: 3 });
const record = await client.getRecord("docs", "a"); // undefined on 404
await client.deleteRecord("docs", "a"); // boolean — true iff a record was actually deleted
```

## Ingestion and search

```ts
const result = await client.ingestText("docs", { id: "handbook", text: "..." });
// result.status is "ingested" | "unchanged" | "partial" — a partial ingest is returned, never
// thrown (root rule 11): the hub answers it as a 500-with-a-body because the document is
// half-embedded, but the document id and the chunks that landed are real.

await client.ingestFile("docs", {
  id: "handbook-pdf",
  filename: "handbook.pdf",
  body: new Blob([bytes], { type: "application/pdf" }),
});

const found = await client.search("docs", "when is payroll");
for (const hit of found.hits) {
  console.log(hit.documentId, hit.score, hit.text); // wire order, never re-sorted by score
}
```

`search` on a collection that does not exist **throws** rather than returning empty hits (root
rule 12) — reporting "no hits" for a misspelled collection name is how a retrieval system reports
an empty corpus as a working one.

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
Both shapes a real hub has sent for `X-InferHub-Sources` — a JSON array and a comma-separated
string — are handled.

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
the C#, Python and (later) Go runners read. Cases whose `kind` is outside `v0.2.0`'s surface
(`probe`, the OpenAI dialect) are skipped by name, not filtered out of the file — 7 cases covered,
6 skipped, all 13 accounted for.

## License

MIT — see [LICENSE](LICENSE).
