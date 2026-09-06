# python/v0.2.0 — retrieval

The second Python release: the vector data-plane, the `X-InferHub-Retrieve*` RAG headers, ingestion
and search. Modalities, admin and the node land in `1.0.0` (phase 18).

## New

- **Vector data-plane** — `upsert`, `query`, `retrieve`, `get_record`, `delete_record` on both
  client classes, over `/api/vector/{collection}/**`. `VectorUpsert.from_vector`/`.from_text` and
  `VectorQuery.from_vector`/`.from_text` mirror the C# client's builder shape.
- **RAG headers** — `chat`/`generate`/`chat_stream`/`generate_stream` gained a `retrieval:
  RetrievalOptions | None` keyword. A call-scoped argument, not a field on the request body: it
  applies to both endpoints and keeps the body serializer from having to know about a header-only
  concern. `X-InferHub-Retrieve` unavailable is HTTP 424, now raised as
  `InferHubRetrievalException` (a subclass of `InferHubError`) rather than the generic base type.
- **Ingestion and search** — `ingest_text`, `ingest_file` (multipart, the file field last),
  `list_documents`, `get_document`, `get_chunks`, `delete_document`, `search` (a plain string or a
  `SearchRequest` — Python has no method overloads), all in a new `_corpus.py` mixed into both
  client classes. Search hits are returned in the hub's own wire order and never re-sorted by
  score — a reranked hit list routinely has a lower score above a higher one.
- **A partial ingest is data, not an exception.** The hub answers a partial ingest with HTTP 500
  and a real body (`documentId`, `chunks`, `chunksEmbedded`, `error`); `ingest_text`/`ingest_file`
  return an `IngestResult` for that shape instead of raising, so the document id and the chunks
  that did land are not thrown away. A genuine error body (no `documentId`/`status`) still raises
  `InferHubError` normally.
- `DocumentChunk.index` is typed `str` — the hub's chunk metadata is a string map — while `page` on
  the same response is a real `int`, exactly the asymmetry the conformance corpus's
  `chunk-index-is-a-string-not-an-int` case exists to catch.

## Bug found and fixed the same day

`build_headers()` set a client-level default `Content-Type: application/json` since `v0.1.0`.
`httpx` merges a client default header over what it would otherwise compute per request, so every
multipart call this phase added (`ingest_file`) silently sent `application/json` instead of a real
multipart boundary — confirmed with `httpx.Client.build_request` before touching any client code,
not assumed. Fixed by dropping the default entirely: `httpx` sets the correct content type for
both `json=` and `files=` calls on its own when nothing forces one. `chat`/`generate`/`embed` were
never affected — `json=` always won regardless of the client default — which is exactly why this
had never been noticed.

## Verified against a real, running InferHub coordinator — and one thing that could not be

This repo's own machine runs a live 3.37.0 coordinator. Verified there: a plain `chat()` (model
`gemma:2b`, a real answer, `served_by: "node"`), and `chat(..., retrieval=RetrievalOptions(...))`
against that same hub raised `InferHubRetrievalException` with status 424 and the hub's exact
message — matched against `curl` hitting the identical route first, byte for byte.

**Not established: `upsert`/`query`/`ingest_text`/`search` against a live collection.** This hub's
own vector/corpus provider reports `"status": "stopped"` on `/api/status`, so every one of those
routes answers `404` regardless of what this client sends — confirmed with raw `curl` against
`/api/collections/.../documents` and `/api/vector/.../upsert` before concluding anything, not
assumed from the status field alone. Enabling that provider is a change to the InferHub server
itself, outside a client-library phase's scope. This is the mocked-suite-only gap for exactly the
routes phase 16's rule ("verify against a live hub, not just mocks") would otherwise have covered;
said out loud rather than silently claimed.

## Compatibility

Additive over `v0.1.0`: `retrieval=` defaults to `None` on every call that gained it, and every new
method is new. `httpx` is still the only runtime dependency. `pytest python/tests`: 53 pass, 6 skip
(named: the node and the OpenAI dialect are phase 18's).

See `python/README.md` for the full API table and `python/examples/mini_rag.py` for a runnable
ingest → search → grounded-chat script.
