# go/v0.1.0 — the core client

The fourth and last language of the polyglot-clients track: `github.com/Dev-Art-Solutions/InferHub.Clients/go`,
covering the Ollama-dialect core surface — chat, generate (blocking and streaming), embeddings
(batch + legacy), model listing, status and health, auth, and the error model. Retrieval (vectors,
RAG, ingestion, search) lands in `0.2.0`; modalities, admin and the node in `1.0.0` — the same D3
plane split every language in this repository already took.

## New

- **`Client`**, constructed via `NewClient(ClientOptions)` — stdlib `net/http` only, zero
  dependencies (`go.mod` has no `require` block and no `go.sum`). Every method takes
  `context.Context` first, per Go idiom, and returns `(T, error)` — no exceptions.
- `Chat`/`Generate`/`Embed`/`EmbedLegacy`/`ListModels`/`Status`/`Ping` return values directly;
  `ChatStream`/`GenerateStream` return a `bufio.Scanner`-shaped iterator
  (`Next()`/`Value()`/`Err()`/`Close()` — the same shape as `database/sql.Rows`) over a shared
  NDJSON line-buffering reader. A mid-stream `{"error": …}` chunk surfaces from `Next`/`Err` rather
  than hanging, same guarantee the corpus enforces in every other language.
- One flat `*Error` type with a `Kind` discriminator (`KindPlain`/`KindRetrieval`/`KindOpenAI`)
  rather than a type hierarchy — Go's embedding rules make a `python`/`js`-style base-and-subtype
  shape a trap here: naming a base type `Error` and anonymously embedding `*Error` in a wrapper
  collides the embedded field's implicit name with its own promoted `Error()` method, silently
  breaking the wrapper's `error` interface. Recorded as its own design decision because it is a
  Go-only correction, not a wire discrepancy — nothing for the conformance corpus.
- `JSONDict`-typed `Extra` fields carry unknown request/response fields, the Go equivalent of
  Python's `.extra` dict, TypeScript's `extra` record and C#'s `[JsonExtensionData]` bag.
- `X-InferHub-Served-By` and `X-InferHub-Sources` (JSON array, with the comma-separated fallback a
  real hub has sent) are read into every response even though `v0.1.0` has no way yet to *request*
  retrieval — same early-read choice `python/v0.1.0` and `js/v0.1.0` both made, for the same
  cross-language-conformance reason.

## Verified against a real, running InferHub coordinator — from the published module, not the working copy

`go get github.com/Dev-Art-Solutions/InferHub.Clients/go@v0.1.0` into a clean scratch module outside
the repo, resolved from the Go module proxy off the pushed `go/v0.1.0` tag (no publish step exists
for Go — the proxy serves straight from the tagged public repo). From that installed module: `Status`
(`coordinatorVersion 3.37.0`, 1 node), `ListModels` (29 models), `Chat` against `qwen2.5:0.5b`
(`"Hi!"`), `Embed` against `nomic-embed-text:latest` (768-dim vector), `ChatStream` (30 chunks), and
a real `404` for an unknown model (`model 'definitely-not-a-real-model' not found`) matching the
corpus's plain-string envelope byte for byte.

Also verified in this pass — a materially larger gap than usual, called out rather than implied
away: the phase was **implemented with no Go toolchain available** in that environment, so nothing
had actually compiled when the code was written (a Go-only design decision, D1 in the phase's own
notes, exists because of exactly this constraint). Before tagging, a portable Go 1.23.4 was installed
and the full checklist run against the working copy: `go build ./...` and `go vet ./...` clean,
`gofmt -l .` found two files needing reformatting (whitespace only, applied), `go test ./...` green.

## Compatibility

First release — nothing to be additive against yet. `go test ./go/...`: 22 unit tests pass; the
conformance runner drives `conformance/cases.json` directly at 4 pass / 9 named-skip / 13 accounted
for (retrieval, the OpenAI dialect, admin, ingestion/search/chunks and `probe` are all outside this
version's surface, not silently omitted). Go 1.22+.

See `go/README.md` for the full API table and `go/examples/` for runnable `basicchat`,
`streamingchat` and `embeddings` programs.
