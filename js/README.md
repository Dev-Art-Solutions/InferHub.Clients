# inferhub-client — the TypeScript client

[![npm](https://img.shields.io/npm/v/inferhub-client.svg)](https://www.npmjs.com/package/inferhub-client)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A small, typed TypeScript client for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) — a
self-hosted, Ollama-compatible inference mesh. The **core** surface (chat, generate, streaming,
embeddings, model listing, status, health) shipped in `0.1.0`. **`v0.2.0` added retrieval**: the
vector data-plane, the `X-InferHub-Retrieve*` RAG headers, ingestion and search. **`v1.0.0` adds
audio, images, the admin plane and the node** — this is now a `1.0.0` semver contract: additive-only
from here.

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

## API surface (v1.0.0)

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
| `transcribe(request)` / `transcribeDocument(request)` | `POST /v1/audio/transcriptions` |
| `createSpeech(request)` / `streamSpeech(request)` | `POST /v1/audio/speech` |
| `generateImage` / `editImage` / `createImageVariation` | `POST /v1/images/generations` \| `/edits` \| `/variations` |
| `submitImage*` / `listImageJobs` / `getImageJob` / `watchImageJob` / `openImageContent` / `cancelImageJob` | `/api/images/jobs/**` |
| `listNodes` / `cordon` / `uncordon` / `deregister` | `/api/admin/nodes/**` |
| `listAdminCollections` / `getAdminCollection` / `createAdminCollection` / `dropAdminCollection` / `rebuildAdminCollection` | `/api/admin/vector/collections/**` |
| `streamAdminEvents()` | `GET /api/admin/stream` (SSE) |
| `listProfiles` / `getProfile` / `putProfile` / `deleteProfile` / `getNodeProfile` | `/api/admin/profiles/**` |
| `pullModel` / `deleteModel` / `warmModel` / `pullToolModel` / `deleteToolModel` / `listModelMatrix` / `ensureModel` | `/api/admin/nodes/**/models/**`, `/api/admin/models/**` |
| `queryUsage()` / `listClients()` | `/api/admin/usage`, `/api/admin/clients` |
| `probe()` | `GET /api/status`, discriminated on `mode` — `"hub"` \| `"solo_node"` |
| `getNodeVersion` / `list\|get\|create\|dropNodeCollection` | `/api/version`, `/api/collections/**` (**node-only**) |

The OpenAI chat dialect (`/v1/chat/completions`) stays dotnet-only per the polyglot-clients track's
own design decision — no method here reaches it.

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

## Audio

```ts
const speech = await client.createSpeech({ model: "piper", input: "hello", responseFormat: "wav" });
const bytes = await speech.response.arrayBuffer(); // read-once — the caller consumes the live Response

for await (const chunk of client.streamSpeech({ model: "piper", input: "hello" })) {
  if (chunk.audio) process.stdout.write(chunk.audio);
  if (chunk.type === "speech.audio.done") console.log(chunk.usage);
}

const transcript = await client.transcribe({
  model: "whisper",
  audio: new Blob([bytes]),
  filename: "clip.wav",
});
```

`createSpeech`/`streamSpeech` hand back the live `Response`/`SpeechChunk`s whether or not you asked
for streaming — not one line of caller code differs (dotnet D2). `transcribeDocument` renders
`text`/`srt`/`vtt` and returns it unaltered; `transcribe` always asks the hub for `verbose_json`
regardless of what you request, because those are the fields worth parsing.

## Images

```ts
const picture = await client.generateImage({ model: "sdxl", prompt: "a lighthouse in fog" });
console.log(picture.data[0]?.b64Json); // base64 — the hub stores nothing, so there is no URL

const job = await client.submitImageGeneration({ model: "sdxl", prompt: "a slower render" });
for await (const update of client.watchImageJob(job.id)) console.log(update.state, update.step);
const content = await client.openImageContent(job.id, 0); // read once — a retry is a 410
```

`editImage`/`createImageVariation` are multipart (`FormData`, no dependency); `ImageEditRequest`
and `ImageVariationRequest` are two separate types rather than one with an `operation` field, so
the hub's refusals ("a variation takes no prompt") are unrepresentable in this client's types
instead of merely disallowed.

## Admin and the node

```ts
const admin = new InferHubClient({ baseUrl, apiKey: "sk-admin-token" });

for (const node of await admin.listNodes()) console.log(node.nodeId, node.name);
for await (const event of admin.streamAdminEvents()) console.log(event.event, event.data);

const probe = await client.probe(); // "hub" | "solo_node" — one GET /api/status
if (probe.kind === "solo_node") console.log(probe.nodeStatus?.capabilities);
```

Admin methods live on the same `InferHubClient` as everything else — construct it with an admin
key to reach them, rather than a second class (a plain TS class has no interface-segregation
concern forcing one). `listNodeCollections` (`GET /api/collections`, client key, no replica info)
and `listAdminCollections` (`GET /api/admin/vector/collections`, admin key, replica placement) are
never the same method — different auth, different route, different shape.

`NodeRetrievalInfo.rerank` is typed `string`, not `boolean` — the conformance corpus's founding
case: a real node reports `"none"`/`"llm"`, the config-level rerank mode, not a flag.

## Video

Not in this client. The hub `501`-refuses `GET /v1/videos` and `POST /v1/videos/{id}/remix`
permanently, so a method that could only throw is not published — `VideoErrorCodes.NOT_SUPPORTED`
names the refusal instead. There is no durable id-to-prompt mapping on the hub, so a remix can
never be served; send a new request with the prompt you want.

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
the C#, Python and (later) Go runners read. Cases whose `kind` is outside `v1.0.0`'s surface (the
OpenAI chat dialect, dotnet-only) are skipped by name, not filtered out of the file — 10 cases
covered, 3 skipped, all 13 accounted for.

## License

MIT — see [LICENSE](LICENSE).
