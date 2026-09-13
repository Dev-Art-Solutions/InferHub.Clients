# inferhub — the Go client

A small, stdlib-only Go client for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) — a
self-hosted, Ollama-compatible inference mesh. The **core** surface (chat, generate, streaming,
embeddings, model listing, status, health) shipped in `v0.1.0`; retrieval (the vector data-plane,
the `X-InferHub-Retrieve*` RAG headers, ingestion and search) in `v0.2.0`. **`v1.0.0` closes the
surface**: audio, images (sync and the async job seam), the admin plane, and the node — a semver
promise, additive-only from here.

**Zero dependencies.** `go.mod` has no `require` block — `net/http`, `encoding/json` and `bufio`
are the whole implementation. Go is the fourth and last language this repository ships (after C#,
Python and TypeScript); the wire is fixed by [`conformance/cases.json`](../conformance/README.md),
the same corpus every other client is driven against.

## Install

```
go get github.com/Dev-Art-Solutions/InferHub.Clients/go@v1.0.0
```

A Go module in a subdirectory resolves **only** from a tag prefixed with that subdirectory — this
is why every language in this repository tags `<lang>/vX.Y.Z` rather than Go being special-cased.

## Quick start

```go
package main

import (
	"context"
	"fmt"
	"log"

	inferhub "github.com/Dev-Art-Solutions/InferHub.Clients/go"
)

func main() {
	client, err := inferhub.NewClient(inferhub.ClientOptions{
		BaseURL: "http://localhost:5080/",
		APIKey:  "sk-client-token-1",
	})
	if err != nil {
		log.Fatal(err)
	}

	answer, err := client.Chat(context.Background(), inferhub.ChatRequest{
		Model:    "llama3",
		Messages: []inferhub.ChatMessage{{Role: "user", Content: "Say hi in one word."}},
	})
	if err != nil {
		log.Fatal(err)
	}
	fmt.Println(answer.Message.Content)
}
```

Every method takes a `context.Context` as its first argument, and errors come back as values —
`error`, never a panic. A non-success HTTP status is always `*inferhub.Error`, with a `Kind` field
saying which envelope produced it — see Errors, below.

## API surface (v1.0.0)

| Method | Endpoint |
|---|---|
| `ListModels(ctx)` | `GET /api/tags` |
| `Chat(ctx, request, retrieval...)` | `POST /api/chat` with `stream:false` → `(ChatResponse, error)` |
| `ChatStream(ctx, request, retrieval...)` | `POST /api/chat` with `stream:true` → `(*ChatStream, error)` |
| `Generate(ctx, request, retrieval...)` | `POST /api/generate` with `stream:false` → `(GenerateResponse, error)` |
| `GenerateStream(ctx, request, retrieval...)` | `POST /api/generate` with `stream:true` → `(*GenerateStream, error)` |
| `Embed(ctx, request)` | `POST /api/embed` (batch — a string or a `[]string`) |
| `EmbedLegacy(ctx, request)` | `POST /api/embeddings` (legacy single prompt) |
| `Status(ctx)` | `GET /api/status` |
| `Ping(ctx)` | `GET /health` — `bool`, never errors for a non-success status |
| `Upsert(ctx, collection, upsert)` | `POST /api/vector/{collection}/upsert` |
| `Query(ctx, collection, query)` | `POST /api/vector/{collection}/query` |
| `Retrieve(ctx, collection, query)` | `POST /api/vector/{collection}/retrieve` (RAG-oriented alias of `Query`) |
| `GetRecord(ctx, collection, id)` | `GET /api/vector/{collection}/{id}` → `(record, ok, error)`, `ok=false` on 404 |
| `DeleteRecord(ctx, collection, id)` | `DELETE /api/vector/{collection}/{id}` → `(deleted bool, error)` |
| `IngestText(ctx, collection, document)` | `POST /api/collections/{collection}/documents` (JSON body) |
| `IngestFile(ctx, collection, document)` | `POST /api/collections/{collection}/documents` (multipart) |
| `ListDocuments(ctx, collection)` | `GET /api/collections/{collection}/documents` |
| `GetDocument(ctx, collection, id)` | `GET /api/collections/{collection}/documents/{id}` → `(doc, ok, error)`, `ok=false` on 404 |
| `GetChunks(ctx, collection, id)` | `GET /api/collections/{collection}/documents/{id}/chunks` |
| `DeleteDocument(ctx, collection, id)` | `DELETE /api/collections/{collection}/documents/{id}` → `(deletion, ok, error)`, `ok=false` on 404 |
| `Search(ctx, collection, request)` | `POST /api/collections/{collection}/search` — errors on a missing collection |
| `Transcribe(ctx, request)` / `TranscribeDocument(ctx, request)` | `POST /v1/audio/transcriptions`, multipart |
| `CreateSpeech(ctx, request)` / `StreamSpeech(ctx, request)` | `POST /v1/audio/speech` |
| `GenerateImage`/`EditImage`/`CreateImageVariation(ctx, request)` | `POST /v1/images/{generations,edits,variations}` |
| `SubmitImageGeneration`/`SubmitImageEdit`/`SubmitImageVariation(ctx, request)` | `POST /api/images/jobs` |
| `ListImageJobs(ctx)` / `GetImageJob(ctx, id)` / `WatchImageJob(ctx, id)` | `GET /api/images/jobs[/{id}[/events]]` |
| `OpenImageContent(ctx, id, index)` / `CancelImageJob(ctx, id)` | `GET`/`DELETE /api/images/jobs/{id}/...` |
| `ListNodes`/`Cordon`/`Uncordon`/`Deregister(ctx, ...)` | `GET/POST /api/admin/nodes/**` |
| `ListAdminCollections`/`GetAdminCollection`/`CreateAdminCollection`/`DropAdminCollection`/`RebuildAdminCollection` | `/api/admin/vector/collections/**` |
| `StreamAdminEvents(ctx)` | `GET /api/admin/stream` (SSE) |
| `ListProfiles`/`GetProfile`/`PutProfile`/`DeleteProfile`/`GetNodeProfile` | `/api/admin/profiles/**`, `/api/admin/nodes/{id}/profile` |
| `PullModel`/`DeleteModel`/`WarmModel`/`PullToolModel`/`DeleteToolModel`/`ListModelMatrix`/`EnsureModel` | `/api/admin/nodes/{id}/models/**`, `/api/admin/models` |
| `QueryUsage(ctx, query)` / `ListClients(ctx)` | `GET /api/admin/usage`, `GET /api/admin/clients` |
| `Probe(ctx)` | `GET /api/status`, discriminated hub vs. solo node |
| `GetNodeVersion(ctx)` | `GET /api/version` (node-only) |
| `ListNodeCollections`/`GetNodeCollection`/`CreateNodeCollection`/`DropNodeCollection` | `/api/collections/**` (node-only, not the admin-gated route) |

No video module — the hub permanently `501`-refuses video listing/remix, so no throw-only method
is published (root rule 10), same call the C#, Python and TypeScript clients already made. No
`/v1/chat/completions` OpenAI-dialect client either — that stays dotnet-only.

## Streaming

```go
stream, err := client.ChatStream(ctx, inferhub.ChatRequest{
	Model:    "llama3",
	Messages: []inferhub.ChatMessage{{Role: "user", Content: "Count to five."}},
})
if err != nil {
	log.Fatal(err)
}
defer stream.Close()

for stream.Next() {
	chunk := stream.Value()
	if chunk.Message != nil {
		fmt.Print(chunk.Message.Content)
	}
}
if err := stream.Err(); err != nil {
	log.Fatal(err) // includes a mid-stream terminal error chunk
}
```

The hub's streaming responses are newline-delimited JSON (NDJSON), never actual
`text/event-stream`. `*ChatStream`/`*GenerateStream` are `bufio.Scanner`-shaped iterators —
`Next()`/`Value()`/`Err()`/`Close()`, the same shape as `database/sql.Rows` and `bufio.Scanner`
itself — rather than a Go 1.23 `iter.Seq2[T, error]` or a channel. See the design-decision comment
at the top of `stream.go` for why: in short, this package was written without a local Go toolchain
to compile-check against, and a `range`-over-func generic is exactly the kind of syntax an author
gets subtly wrong without a compiler; the Scanner shape needs no goroutine either, so there is
nothing to leak if a caller stops iterating early. A terminal error chunk
(`{"error": "...", "done": true}`) surfaces from `Next`/`Err` instead of the loop hanging or ending
quietly with a partial answer nobody was told about.

## Retrieval

`RetrievalOptions` is a **trailing, variadic parameter** on `Chat`/`ChatStream`/`Generate`/
`GenerateStream` — omit it for a plain call (a `v0.1.0` call site compiles and behaves identically),
or pass one to build the `X-InferHub-Retrieve*`/`X-InferHub-Rerank` headers for that call only. It
is never a field on `ChatRequest`/`GenerateRequest`: retrieval is a call-scoped concern that applies
to two different request shapes, same argument python 17 and js 20 make for their languages.

```go
k := 5
answer, err := client.Chat(ctx, inferhub.ChatRequest{
	Model:    "llama3",
	Messages: []inferhub.ChatMessage{{Role: "user", Content: "What is InferHub?"}},
}, inferhub.RetrievalOptions{Collection: "docs", K: &k})
if err != nil {
	var inferErr *inferhub.Error
	if errors.As(err, &inferErr) && inferErr.Kind == inferhub.KindRetrieval {
		// retrieval was asked for and is unavailable (HTTP 424) — the chat call itself could have
		// succeeded; only the retrieval step it depended on could not.
	}
}
fmt.Println(answer.Message.Content, answer.SourceIDs)
```

### Vector data-plane

`Upsert`/`Query`/`Retrieve`/`GetRecord`/`DeleteRecord` on `*Client` — `POST/GET/DELETE
/api/vector/{collection}/**`. `GetRecord` and `DeleteRecord` return an `ok bool` alongside the
value/error instead of erroring on a 404 (root rule 12: a 404 naming one record is an absence, not
a failure):

```go
match, err := client.Upsert(ctx, "docs", inferhub.VectorUpsert{ID: "a1", Text: "hello"})
matches, err := client.Query(ctx, "docs", inferhub.VectorQuery{Text: "hello", TopK: 5})
record, ok, err := client.GetRecord(ctx, "docs", "a1") // ok == false, err == nil on a 404
```

### Ingestion and search

`IngestText`/`IngestFile`/`ListDocuments`/`GetDocument`/`GetChunks`/`DeleteDocument`/`Search` —
`/api/collections/{collection}/**`. `IngestFile` builds its own `multipart.Writer` (stdlib
`mime/multipart`, no dependency); the file field is written last, matching the ordering the C#,
Python and TypeScript clients already use for parsers that are order-sensitive.

**A `"partial"` ingest is data, not a failure.** The hub answers a partial ingest with an HTTP 500
that carries a complete `IngestResult` body — `IngestText`/`IngestFile` return that value instead of
an error (root rule 11); only a body that does *not* look like an `IngestResult` (no `documentId` +
`status`) still falls back to the usual error mapping.

```go
result, err := client.IngestText(ctx, "docs", inferhub.TextDocument{ID: "d1", Text: "..."})
if err == nil && result.Status == "partial" {
	fmt.Println("partial:", result.Error) // which node/model refused, not a wasted document
}

found, err := client.Search(ctx, "docs", inferhub.SearchRequest{Query: "hello"})
// found.Hits stays in the hub's own wire order — never re-sorted by score. A reranked result
// routinely puts a lower score above a higher one; sorting "to be tidy" undoes what the caller paid
// for the rerank to do.
```

`DocumentChunk.Index` is a **string**, not an int — the hub's chunk metadata is a string map, and
`Page`, when present, is a real number on the same response. The conformance case
`chunk-index-is-a-string-not-an-int` exists because a client that gets this backwards fails to
deserialize the very shape it is supposed to read.

## Audio and images

`Transcribe` always requests `verbose_json` regardless of what `TranscriptionRequest.ResponseFormat`
says (dotnet D6 — these are the fields a caller does something with); use `TranscribeDocument` for
`text`/`srt`/`vtt`, returned unaltered. `CreateSpeech` hands back the live `*http.Response` whether
or not streaming was asked for — the caller consumes `Response.Body`; nothing buffers it (root rule
7). `StreamSpeech` forces SSE framing and yields `SpeechChunk`s, ending at `speech.audio.done`; a
`speech.audio.error` frame surfaces as `*inferhub.Error` with `Kind == KindOpenAI` instead of
silently ending the stream.

```go
audio, err := client.CreateSpeech(ctx, inferhub.SpeechRequest{Model: "piper", Input: "hello"})
defer audio.Response.Body.Close()
io.Copy(out, audio.Response.Body)
```

Image generation/edit/variation is `GenerateImage`/`EditImage`/`CreateImageVariation` (sync,
`/v1/images/*`) or `SubmitImageGeneration`/`SubmitImageEdit`/`SubmitImageVariation` (async,
`/api/images/jobs` — poll with `GetImageJob` or stream with `WatchImageJob`, an SSE iterator that
stops at a terminal state). `ImageOptions` is a struct of `*int`/`*float64`/`string` fields, turned
into the `X-InferHub-Image-*` extension headers — never body fields. `OpenImageContent` is read
once: the hub unlinks the bytes as they are read, so a retry is a `410`.

## The admin plane and the node

Fleet ops (`ListNodes`/`Cordon`/`Uncordon`/`Deregister`), admin-gated vector collections (distinct
from the node-only ones below — different auth, different route, different shape), node profiles
(`PutProfile`'s `NodeProfile.Name`/`.Revision` are ignored on write — the hub sets both from the
route and its own counter), model lifecycle commands, and `QueryUsage` (counts only, never a prompt
or a completion — hub rule 7) all need an admin key on `ClientOptions.APIKey`.

`Probe` is the node story: one `GET /api/status`, discriminated on whether the body carries `mode`
(present → a solo node; the hub's own document never has the field at all — the conformance
corpus's `hub-status-has-no-mode-field` case exists to pin exactly that absence):

```go
probe, err := client.Probe(ctx)
switch probe.Kind {
case inferhub.TargetHub:
	fmt.Println(*probe.HubStatus.CoordinatorVersion, len(probe.HubStatus.Nodes), "nodes")
case inferhub.TargetSoloNode:
	fmt.Println(probe.NodeStatus.Name, probe.NodeStatus.Retrieval.Rerank) // Rerank is a STRING
}
```

`NodeStatusResponse.Retrieval.Rerank` is typed `string` (`"none"`/`"llm"`), never `bool` — the
conformance corpus's founding case: dotnet `v1.7.0` typed it `bool?` and threw the first time it was
driven against a real node with retrieval on, fixed same-day in `v1.7.1`. This client is typed
correctly from the start because the case existed before it did.

A solo node also answers `GET /api/version` (`GetNodeVersion` — a 404 against a hub means "wrong
target," not "wrong version") and its own `/api/collections` vector-collection lifecycle
(`ListNodeCollections`/`GetNodeCollection`/`CreateNodeCollection`/`DropNodeCollection`) — not the
admin-gated `/api/admin/vector/collections` route: a node has no fleet to place a replica on, so the
shape is smaller and the auth is different.

## Video

Not in this client. The hub `501`-refuses `GET /v1/videos` and `POST /v1/videos/{id}/remix`
permanently, so a method that could only return an error is not published — `inferhub.
VideoErrorCodeNotSupported` names the refusal instead. There is no durable id-to-prompt mapping on
the hub, so a remix can never be served; send a new request with the prompt you want.

## Errors

Every non-success response returns `*inferhub.Error`. Which envelope arrived decides its `Kind`,
never which method was called: `/api/*` answers `{"error":"..."}` → `KindPlain`; `/v1/*` and routes
that reuse its shape answer `{"error":{"message":...,"type":...,"param":...,"code":...}}` →
`KindOpenAI` (`Code`/`Param`/`Type` populated). A `424` — retrieval asked for and unavailable —
always returns `KindRetrieval`.

This is a **flat type with a discriminator field**, not the class hierarchy python's
`RetrievalError(InferHubError)` and js's `class InferHubRetrievalException extends InferHubError`
use: Go has no inheritance, and the direct translation (an embedded `*Error` inside a
`RetrievalError`/`OpenAIError` wrapper struct) has a real trap when the base type is named `Error` —
the embedded field's implicit name collides with its own promoted `Error()` method, and the
promotion silently loses, so the wrapper stops satisfying the `error` interface. See the
design-decision comment at the top of `errors.go` for the full argument; in short, `Kind` is lower
risk and arguably more idiomatic Go besides (`errors.Is`/`os.IsNotExist`-style discrimination over a
type hierarchy).

```go
import "errors"

_, err := client.Embed(ctx, inferhub.EmbedRequest{Model: "nomic-embed-text", Input: "hello"})
var inferErr *inferhub.Error
if errors.As(err, &inferErr) {
	fmt.Println(inferErr.StatusCode, inferErr.Message, inferErr.RetryAfter)
	if inferErr.Kind == inferhub.KindRetrieval {
		// retrieval was asked for and is unavailable — the chat/generate call itself could have
		// succeeded; only the retrieval step it depended on could not.
	}
}
```

`RetryAfter` (`*float64`, seconds) is populated from a `Retry-After` header when the hub sends
one — the refusals that carry it are the ones worth retrying rather than only reporting.

## `Extra`: the fields this version does not know about yet

`ChatMessage`, `ChatRequest`, `GenerateRequest` all carry an `Extra JSONDict` (`JSONDict` is
`map[string]any`), merged straight into the request body on the way out (Ollama's `options`,
`format`, `keep_alive`, tool definitions — anything the hub accepts that this client has not typed);
`ChatMessage`, `ChatResponse`, `GenerateResponse` and `StatusResponse` carry an `Extra` built from
whatever fields were not consumed on the way in. Typing every Ollama option was considered and
rejected, same as the C#, Python and TypeScript clients: the hub owns that schema and grows it
independently of this package's release cadence.

```go
_, err := client.Chat(ctx, inferhub.ChatRequest{
	Model:    "llama3",
	Messages: []inferhub.ChatMessage{{Role: "user", Content: "hi"}},
	Extra: inferhub.JSONDict{
		"tools": []any{inferhub.JSONDict{"type": "function", "function": inferhub.JSONDict{"name": "get_weather"}}},
	},
})
```

## `ServedBy` and `SourceIDs`

Every `ChatResponse`/`GenerateResponse` carries `ServedBy` (which node or `provider:<id>` answered)
and `SourceIDs` (retrieval source document ids), read from response headers — surfaced, never
interpreted (root `CLAUDE.md` rule 8). This client does not route, retry elsewhere, or prefer on
`ServedBy`: deciding to re-send a prompt to a second address is a second disclosure of the same
prompt. `X-InferHub-Sources` is parsed even though `v0.1.0` has no way yet to opt into retrieval,
and both shapes a real hub has sent — a JSON array and a comma-separated string — are handled.

## A node is a base address

A solo InferHub node serves the same paths with the same bodies as a coordinator, so pointing this
client at a node's own address is the whole of "run it against a node" (root `CLAUDE.md` rule 6).
There is no separate node client type, and none is planned. See "The admin plane and the node",
above, for `Probe` and the node-only routes.

## Development

```
go build ./go/...
go vet ./go/...
gofmt -l go/
go test ./go/...
```

`conformance_test.go` drives the shared corpus at `../conformance/cases.json` — the same file the
C#, Python and TypeScript runners read. Cases whose `kind` is outside this version's surface (the
OpenAI `/v1/chat/completions` dialect only, which stays dotnet-only) are skipped by name via
`t.Skip`, not filtered out of the file — **10 cases covered, 3 skipped**, all 13 accounted for (the
same split js/v1.0.0 reached over the identical corpus).

Verified with a portable Go 1.23.4 install: `go build ./...` and `go vet ./...` clean, `gofmt -l .`
clean, `go test ./...` green — unit tests plus the conformance runner at 10 pass / 3 named-skip /
13 accounted for, including the three cases this version newly covers (`node-status-rerank-is-a-
string`, `hub-status-has-no-mode-field`, `503-capability-unavailable-carries-retry-after`).

## License

MIT — see the repository root [LICENSE](../LICENSE).
