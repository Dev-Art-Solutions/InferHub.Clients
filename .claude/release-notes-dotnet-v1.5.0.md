# InferHub.Client 1.5.0 — documents in, chunks out, and a search whose order is the answer

**Phase 12 of `InferHub.Clients`.** The C# client can now fill the corpus it has been able to query
since 1.0: upload a document, list what is in a collection, read the chunks it actually became, and
run the retrieval the RAG path runs — with the matches visible instead of folded into a prompt.

Additive, as every release on the `1.x` line is. Nothing in 1.4 changed shape, nothing was renamed,
and a caller compiled against 1.4 compiles against this unchanged.

```
dotnet add package InferHub.Client --version 1.5.0
```

## What is new

**`IInferHubCorpusClient`** — one interface for the corpus plane, registered by
`AddInferHubClient` alongside the five that were already there.

| Method | Endpoint |
|---|---|
| `IngestTextAsync` | `POST /api/collections/{c}/documents` (JSON — `text` is the document) |
| `IngestFileAsync` | `POST /api/collections/{c}/documents` (multipart — text, Markdown, HTML, JSON, PDF) |
| `ListDocumentsAsync` | `GET /api/collections/{c}/documents` |
| `GetDocumentAsync` | `GET /api/collections/{c}/documents/{id}` (→ `null` on 404) |
| `GetChunksAsync` | `GET /api/collections/{c}/documents/{id}/chunks` (→ empty on 404) |
| `DeleteDocumentAsync` | `DELETE /api/collections/{c}/documents/{id}` (→ `null` on 404) |
| `SearchAsync` | `POST /api/collections/{c}/search` |

**`RetrievalOptions` gained `Mode` and `Rerank`** — `X-InferHub-Retrieve-Mode` and
`X-InferHub-Rerank` on chat and generate, in both dialects. Until now a caller could ask for hybrid
retrieval in the search playground and not in the chat that ships.

## The two shapes this release exists to get right

**A `partial` ingest is an HTTP `500` carrying a complete body, and it is returned rather than
thrown.** The hub answers an error status on purpose — a half-ingested document that claims success
is worse than a failure — but the body is the outcome, not an error page: the chunks that embedded
are really in the store, and re-posting the same bytes resumes rather than duplicating. A client
that mapped every `5xx` onto an exception would throw away the document id needed to resume. So
`IngestResult.IsPartial` and `Error` carry it, and only a `500` whose body is *not* a partial result
throws.

```json
{"documentId":"z","collection":"handbook","status":"partial","chunks":1,"chunksEmbedded":0,
 "bytes":11,"contentHash":"12998c01…","error":"no node is advertising embedding model 'no-such-embed-model'"}
```

**A reranked search comes back in an order its own scores contradict.** The reranker sorts the
candidates by what the model said and leaves every `score` exactly as retrieval computed it, so a
reranked hit list routinely starts with a *lower* score than the hit below it. Recorded from a
live hub, for the question *"how do I get an expense approved"*:

| # | `documentId` | `score` |
|---|---|---|
| 0 | `policy.txt` — *"Expenses over 500 EUR need approval from a line manager"* | `0.0164` |
| 1 | `onboarding` — *"Error E-4021 means…"* | `0.0325` |

Sorting those by `score` puts the E-4021 chunk on top of an answer about expense approval — it
silently undoes the rerank the caller asked for and paid a chat round trip for. `SearchResponse.Hits`
is therefore handed over in wire order, and `SearchHit.Score` is documented as *what retrieval
scored, not what ranked it*.

## Smaller things the surface makes explicit

- **Two ingest methods, not one with everything nullable.** The hub picks its reader off the content
  type, so text and a file are two calls rather than one record whose wrong half is a runtime `400`.
- **Every form field is written before the file part**, and a test asserts the order in the produced
  body. Above the hub's `Tools:MaxStreamedBytes` a request is routed from its leading fields while
  the bytes are still arriving; below that ceiling any order works, which is what makes the mistake
  survive every test that only checks the fields are present.
- **This is the one multipart surface that sends the caller's file name** — the hub resolves the
  extractor from the extension, stores it as each chunk's `source`, and falls back to it for the
  document id. An image upload deliberately drops it. The doc comment says so, because the name
  lands in the corpus and comes back on search hits.
- **A missing document is an absence and a missing collection is an error.** `GetDocumentAsync`
  answers `null`; `SearchAsync` on a collection that does not exist throws, because answering "no
  hits" for a name with a typo in it is how a retrieval system reports an empty corpus as a working
  one.
- **`index` and `page` are strings on the chunks route** and `page` is an `int` on a search hit —
  the same chunk described in two types, because one route hands back chunk metadata (a string map)
  and the other a parsed match. `IndexOrDefault` and `PageOrDefault` parse invariantly.
- **An ingest reports three words and a document reports two.** `ingested` | `unchanged` | `partial`
  against `complete` | `partial`. Two constant classes, `IngestStatuses` and `DocumentStatuses`, so
  the sets cannot be confused for one.
- **`424` keeps `InferHubRetrievalException`.** Retrieval-unavailable is one condition in both
  dialects and does not gain a second exception type.
- The corpus client is registered with an **infinite `HttpClient.Timeout`** and the configured
  `Timeout` applied per call: an ingest is extracted, chunked and embedded on the fleet before it
  answers, so a long document is not a 100-second call.

## Dependencies, trimming, AOT

Unchanged. Two `PackageReference`s — `Microsoft.Extensions.Http` and
`…DependencyInjection.Abstractions` — `<IsAotCompatible>true</IsAotCompatible>`, zero trim and zero
AOT warnings, every new DTO through `InferHubJsonContext`. Multipart is `MultipartFormDataContent`
from the BCL.

## Tests

**227 per TFM: 224 pass, 3 skip** (up from 197: 194 pass, 3 skip). The three skips are the
env-gated integration suite, which runs only when `INFERHUB_TEST_BASEADDRESS` is set. **Skipped is
not passed**, and that is the same three tests as in 1.4.0.

## What was recorded, and what was not

**Every payload in `InferHubCorpusClientTests` was recorded from a live InferHub 3.37.0** on
2026-09-02 by driving the routes and pasting what came back — the `ingested` and `unchanged`
results, the `partial` `500`, the document list, the chunks with their string-typed `index`, the
delete, the vector, keyword and hybrid searches, **the reranked search above**, the `424`, the
`415`s and the two `404`s.

The target was a **standalone node in solo mode** with `LocalApi:Retrieval:Enabled=true`, embedding
with `nomic-embed-text:latest` and reranking with `llama3.1:latest` — not the always-on hub, which
runs with `VectorStore:Enabled=false`, where these routes are not mapped at all and answer a **404
with an empty body** (itself now recorded in `spec/README.md`). That is not a weaker recording:
ingestion, chunking, the document index and the search pipeline live in `InferHub.Shared` and the
node runs the coordinator's own code. Two differences are real and both are in the test file — PDF
is a `415` on a node, and the node writes `"error":null` where the coordinator omits the field.

**Said out loud, because it is the honest state:**

- **A mixed partial ingest was not observed.** The recorded `partial` is the case where *every*
  batch failed, which a missing embedding model produces on demand. One where some batches succeed
  and others do not needs a fleet that fails halfway through a document, and no such failure was
  arranged. The client's behaviour does not branch on which it is — it reads the body either way —
  but the payload is one case, not two.
- **`chunksEmbedded: 0` on a partial leaves no document at all.** Recorded: the `500` names an id
  that `GET …/documents/{id}` then answers `404` for. "Partial" is a statement about the call, not
  a promise that something is retrievable.
- **No coordinator-side recording.** Everything above came from a node. A hub with retrieval enabled
  would exercise the same shared pipeline plus PDF extraction and node-owned-collection dispatch,
  neither of which is covered here. Phase 25 is where that is closed.
- **The multipart field-order refusal was not reproduced**, because document ingestion is buffered
  on 3.37.0 (`ReadFormAsync`) and the streamed path is a config key away. The discipline is asserted
  against the client's own produced body instead of against a hub that refuses it.
- **The conformance corpus does not exist yet** (phase 15). The new shapes were written into
  `spec/README.md` so that phase picks them up.

## Verified from the published package

`InferHub.Client 1.5.0` was installed **from nuget.org** into a clean console project that has never
seen this working copy, and driven against a live InferHub 3.37.0 — the same solo node with
retrieval on. Not the working copy, and not the test suite: a green suite says nothing about what
was packed.

`samples/Ingest`, copied in unchanged, ingested a document into a collection it provisioned, listed
it, read its chunks and ran a hybrid reranked search. Then each claim above was checked one at a
time:

| Check | Result |
|---|---|
| A `partial` ingest is returned, not thrown | `IsPartial: true`, id `partial-probe` kept, `0/1` embedded, `"no node is advertising embedding model 'no-such-embed-model'"` surfaced |
| The same bytes twice | `ingested` then `unchanged` |
| A missing **document** | `null` |
| A missing **collection** on search | threw `404 — collection '…' does not exist` |
| `424` on search | `InferHubRetrievalException` with the hub's own sentence |
| `index` on the chunks route | raw `"0"`, `IndexOrDefault` `0`, `page` `null` |
| A file upload's name | became the document id — `verify-notes.md`, `ingested` |
| `DeleteDocumentAsync` | `1` chunk, then `null` on the second call |

The reranked multi-hit ordering was recorded and asserted against the corpus built earlier in the
session rather than in this clean run, which held one document.

## Compatibility

- **Wire:** InferHub 3.6+ for ingestion and search; recorded against 3.37.0. A hub or node with the
  vector store off answers `404` on these routes.
- **API:** additive over 1.4.0. `RetrievalOptions` gained two properties; no signature changed.
- **Frameworks:** `net9.0` and `net10.0`, as before.

## Links

- Package: <https://www.nuget.org/packages/InferHub.Client/1.5.0>
- Repository: <https://github.com/Dev-Art-Solutions/InferHub.Clients>
- Server: <https://github.com/Dev-Art-Solutions/InferHub>
