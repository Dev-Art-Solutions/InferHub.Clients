# go/ — the Go client

**Empty until phase 22.** The folder is a placeholder and contains **no `go.mod`** on purpose: a
module that resolves and does nothing is worse than an absent one, and `go get` would find it.

## What lands here

- **Phase 22** (`go/v0.1.0`) — the core client: chat, generate, streaming, embeddings, models,
  status, auth, the error model.
- **Phase 23** (`go/v0.2.0`) — the vector data plane, RAG headers, ingestion, search.
- **Phase 24** (`go/v1.0.0`) — audio, images, video, admin, a node as a target, and 1.0.

Stdlib `net/http` only, `context.Context` as the first argument, errors as values, an iterator for
streams. Examples in `go/examples/`, run by CI against the conformance stub.

**The module path is `github.com/Dev-Art-Solutions/InferHub.Clients/go` and its tags are
`go/vX.Y.Z`** — a module in a subdirectory resolves only from a tag carrying that prefix. This is
why every language in this repository uses the scheme rather than Go being special-cased.

Go is deliberately **last** of the four: if it is not the boring one, the conformance corpus is not
finished, and that is a useful thing to learn at phase 22 rather than at phase 16.
