# inferhub — the Go client

A small, stdlib-only Go client for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) — a
self-hosted, Ollama-compatible inference mesh. The **core** surface (chat, generate, streaming,
embeddings, model listing, status, health) ships in `v0.1.0`. `v0.2.0` adds **retrieval**: the
vector data-plane, the `X-InferHub-Retrieve*` RAG headers, ingestion and search. `v1.0.0` adds
audio, images, the admin plane and the node.

**Zero dependencies.** `go.mod` has no `require` block — `net/http`, `encoding/json` and `bufio`
are the whole implementation. Go is the fourth and last language this repository ships (after C#,
Python and TypeScript); the wire is fixed by [`conformance/cases.json`](../conformance/README.md),
the same corpus every other client is driven against.

## Install

```
go get github.com/Dev-Art-Solutions/InferHub.Clients/go@go/v0.1.0
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

## API surface (v0.1.0)

| Method | Endpoint |
|---|---|
| `ListModels(ctx)` | `GET /api/tags` |
| `Chat(ctx, request)` | `POST /api/chat` with `stream:false` → `(ChatResponse, error)` |
| `ChatStream(ctx, request)` | `POST /api/chat` with `stream:true` → `(*ChatStream, error)` |
| `Generate(ctx, request)` | `POST /api/generate` with `stream:false` → `(GenerateResponse, error)` |
| `GenerateStream(ctx, request)` | `POST /api/generate` with `stream:true` → `(*GenerateStream, error)` |
| `Embed(ctx, request)` | `POST /api/embed` (batch — a string or a `[]string`) |
| `EmbedLegacy(ctx, request)` | `POST /api/embeddings` (legacy single prompt) |
| `Status(ctx)` | `GET /api/status` |
| `Ping(ctx)` | `GET /health` — `bool`, never errors for a non-success status |

Retrieval, ingestion, search, audio, images, admin and the node are **not in this version** — see
`v0.2.0`/`v1.0.0` in the [root README](../README.md)'s parity table. There is no `Probe` method and
no OpenAI dialect (`/v1/*`) client yet; nothing here is a method that could only return an error.

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
There is no separate node client type, and none is planned.

## Development

```
go build ./go/...
go vet ./go/...
gofmt -l go/
go test ./go/...
```

`conformance_test.go` drives the shared corpus at `../conformance/cases.json` — the same file the
C#, Python and TypeScript runners read. Cases whose `kind` is outside `v0.1.0`'s surface (`probe`,
the OpenAI dialect, retrieval/ingestion/search/chunks) are skipped by name via `t.Skip`, not
filtered out of the file — 4 cases covered, 9 skipped, all 13 accounted for.

**Not verified in this repository's environment:** the Go toolchain (`go`, `gofmt`) is not installed
where this package was written, so none of the commands above have actually been run against this
code — see `plans/phase-22-go-core.md`'s verification section (gitignored locally) and the phase's
release notes for what that means for this release.

## License

MIT — see the repository root [LICENSE](../LICENSE).
