# js/v0.2.0 — retrieval

The second TypeScript release: the vector data-plane, the `X-InferHub-Retrieve*` RAG headers,
ingestion and search. Modalities, admin and the node land in `1.0.0` (phase 21).

## New

- **Vector data-plane** — `upsert`, `query`, `retrieve`, `getRecord`, `deleteRecord` on
  `InferHubClient`, over `/api/vector/{collection}/**`. `getRecord`/`deleteRecord` return
  `undefined`/`false` on a 404 rather than throwing.
- **RAG headers** — `chat`/`chatStream`/`generate`/`generateStream` gained a second, optional
  `retrieval: RetrievalOptions` argument. A call-scoped parameter, not a body field: it applies to
  both endpoints and keeps the request serializer from having to know about a header-only concern
  (same call this repo's Python client made in phase 17, ported to TypeScript's argument shape
  since the language has no keyword-only parameters). `X-InferHub-Retrieve` unavailable is HTTP
  424, thrown as `InferHubRetrievalException`.
- **Ingestion and search** — `ingestText`, `ingestFile` (`FormData`, the platform's own multipart
  encoder — zero new dependency), `listDocuments`, `getDocument`, `getChunks`, `deleteDocument`,
  `search` (a bare string or a `SearchRequest`), all in a new `_corpus.ts` module. Search hits come
  back in the hub's own wire order and are never re-sorted by score — a reranked hit list routinely
  has a lower score above a higher one.
- **A partial ingest is data, not an exception.** The hub answers a partial ingest with HTTP 500
  and a real body (`documentId`, `chunks`, `chunksEmbedded`, `error`); `ingestText`/`ingestFile`
  return an `IngestResult` for that shape instead of throwing, so the document id and the chunks
  that did land are not lost. A genuine error body (no `documentId`/`status`) still throws
  `InferHubError` normally.
- `DocumentChunk.index` is typed `string` — the hub's chunk metadata is a string map — while `page`
  on the same response is a real `number`, exactly the asymmetry the conformance corpus's
  `chunk-index-is-a-string-not-an-int` case exists to catch.
- `search` on a collection that does not exist **throws**, unlike `getDocument`/`getChunks`/
  `deleteDocument`'s 404-is-absence handling: answering "no hits" for a misspelled collection name
  reports an empty corpus as a working one, which is the failure a caller finds six months later.

## Verified against a real, running InferHub coordinator — and one thing that could not be

This repo's own machine runs a live 3.37.0 coordinator. Verified there: a plain `chat()` (model
`gemma:2b`, a real answer, `servedBy: "node"`), and `chat(request, { collection: "handbook" })`
against that same hub threw `InferHubRetrievalException` with `statusCode: 424` — matched against
`curl` hitting the identical route first, byte for byte.

**Not established: `upsert`/`query`/`ingestText`/`search` against a live collection.** Same gap
python 17 and js 19 already recorded for this machine: `/api/status` reports the vector/corpus
provider as `"status": "stopped"`, so every one of those routes answers `404` regardless of what
this client sends — confirmed with raw `curl` against `/api/collections/.../documents` and
`/api/vector/.../upsert` (both 404) before writing any of this, and the client reproduced the same
404 through `ingestText`/`search` in the same session. Enabling that provider is a change to the
InferHub server itself, outside a client-library phase's scope.

## Compatibility

Additive over `v0.1.0`: `retrieval` defaults to `undefined` on every call that gained it, and every
new method is new. `js/package.json`'s `dependencies` is still `{}` — `FormData` is the platform.
`npm -w js test`: 45 pass, 6 skip (named: `probe()` and the OpenAI dialect are phase 21's).

See `js/README.md` for the full API table and `js/examples/mini-rag.ts` for a runnable
ingest → search → grounded-chat script.
