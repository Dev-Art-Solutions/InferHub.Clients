# go/v0.2.0 — retrieval

The second Go phase: `github.com/Dev-Art-Solutions/InferHub.Clients/go` gains the vector
data-plane, the `X-InferHub-Retrieve*`/`X-InferHub-Rerank` RAG headers, and ingestion/search —
the same plane python 17 and js 20 shipped for their languages. Modalities, admin and the node are
still `1.0.0` (phase 24).

## New

- **`RetrievalOptions` as a trailing, variadic parameter** on `Chat`/`ChatStream`/`Generate`/
  `GenerateStream` — `client.Chat(ctx, request)` still compiles and behaves identically to
  `v0.1.0`; `client.Chat(ctx, request, inferhub.RetrievalOptions{...})` builds the five
  `X-InferHub-Retrieve*`/`X-InferHub-Rerank` headers for that call only. Go has no optional
  parameters and no keyword arguments (python 17's and js 20's respective answers to the same
  question), so this is the idiomatic Go equivalent that stays truly additive — the one property
  a required third parameter or a `*RetrievalOptions` pointer both fail in their own way (see D1 in
  `plans/phase-23-go-retrieval.md`).
- **`vector.go`**: `Upsert`/`Query`/`Retrieve`/`GetRecord`/`DeleteRecord`. `GetRecord`/
  `DeleteRecord` return an `ok bool` alongside the value/error rather than erroring on a 404 (root
  rule 12).
- **`corpus.go`**: `IngestText`/`IngestFile`/`ListDocuments`/`GetDocument`/`GetChunks`/
  `DeleteDocument`/`Search`. `IngestFile` builds its own `multipart.Writer` (stdlib
  `mime/multipart` — no dependency), file field last, matching every other language's ordering
  choice. **A `"partial"` ingest (HTTP 500 with a complete `IngestResult` body) is returned as a
  value, never an error** — `ingestResultOrError` checks the body's shape before falling back to
  the usual error mapping (root rule 11). `Search` hits stay in the hub's own wire order, never
  re-sorted by score. `DocumentChunk.Index` is typed `string`, not `int` — the corpus's
  `chunk-index-is-a-string-not-an-int` case exists to catch exactly this.
- `go/examples/minirag/main.go` — ingest two documents, search them, then a grounded chat that
  prints which document answered it. Ported from `js/examples/mini-rag.ts` / python's own.

## Conformance

`conformance/cases.json`'s `ingest-text`/`search`/`chunks` kinds are now covered — 3 more cases:
`partial-ingest-is-a-500-with-a-body-not-thrown`, `reranked-search-order-contradicts-its-own-scores`,
`chunk-index-is-a-string-not-an-int`. Split moves from `v0.1.0`'s 4/13 to **7/13** — the same split
js/v0.2.0 reached over the identical corpus. `probe` and the OpenAI dialect remain outside this
version's surface (`go/v1.0.0`, phase 24).

## Verified against the published module, from the public proxy

`go get github.com/Dev-Art-Solutions/InferHub.Clients/go@v0.2.0` into a clean scratch module outside
the repo, resolved from `proxy.golang.org` off the pushed `go/v0.2.0` tag (no publish step exists
for Go). A small program built against that installed module calling `Chat(..., retrieval)`,
`IngestText` and `Search` compiled and ran — confirming the public API shape a real caller sees
matches the working copy's.

**What was not established**: no InferHub coordinator was reachable from this environment for this
release (no live instance running here at tag time, unlike `go/v0.1.0`'s pass), so the calls above
only confirm compilation and correct request construction — each returned a connection-refused
error, not a hub response. The working-copy `go test ./...` suite (httptest-based, no live hub
required by design — root CLAUDE.md testing-discipline rule) is the actual correctness evidence for
this release, same as every other phase's unit/conformance split. A live-hub pass against this
version is deferred to phase 25 (the dedicated verification day), which already exists in the track
for exactly this reason.

## Compatibility

Fully additive over `v0.1.0`: every existing call site (`Chat(ctx, request)`, `Generate(ctx,
request)`, ...) compiles and behaves identically — the new parameter is a variadic tail that
defaults to empty. `go.mod`'s `require` block stays empty — `mime/multipart` joins `net/http`/
`encoding/json`/`bufio` as stdlib-only. `go test ./...`: unit tests plus the conformance runner at
7 pass / 6 named-skip / 13 accounted for. `go vet ./...` and `gofmt -l .` clean. Go 1.22+.

See `go/README.md` for the full API table (including the new vector/corpus methods) and
`go/examples/minirag` for a runnable end-to-end program.
