# Social copy — go/v0.2.0 (Iliya posts these manually)

## Facebook

> InferHub.Clients' Go client gets retrieval: go/v0.2.0 adds the vector data-plane (upsert/query/
> retrieve/get/delete), the X-InferHub-Retrieve*/X-InferHub-Rerank RAG headers, and ingestion +
> search. Still zero dependencies — mime/multipart from the standard library handles file uploads,
> same as net/http handled everything in v0.1.0.
>
> The interesting Go-specific call here wasn't on the wire, it was in the API shape: Python made
> retrieval a keyword-only argument, TypeScript a second optional positional parameter — Go has
> neither. Landed on a trailing variadic parameter instead (Chat(ctx, request, retrieval...)), which
> is the one shape that's both idiomatic and genuinely additive: every v0.1.0 call site still
> compiles unchanged, nothing has to grow a nil it doesn't care about.
>
> Two rules ported unmodified from the other three languages: a partial ingest arrives as an HTTP
> 500 with a complete result body, and this client returns it as a value instead of turning it into
> an error — the document id and the chunks that did land are real, throwing them away is the actual
> bug. And search results stay in the hub's own order, never re-sorted by score — a reranked hit
> list routinely puts a lower score above a higher one on purpose.
>
> Conformance coverage: 7 of 13 shared cases now pass, matching TypeScript's v0.2.0 split over the
> same corpus. Verified against the published module from the Go proxy, not just the working copy.
>
> Package: pkg.go.dev/github.com/Dev-Art-Solutions/InferHub.Clients/go
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**Keep under 280 characters total, URL included.**

> InferHub.Clients go/v0.2.0: retrieval for the Go client. Vector CRUD, RAG headers via a variadic
> arg (stays additive over v0.1.0), ingestion + search. Still zero deps — mime/multipart is the
> platform. 7/13 conformance cases now pass.
>
> pkg.go.dev/github.com/Dev-Art-Solutions/InferHub.Clients/go

(~255 characters plus the URL — check with `wc -m` before posting.)

## Notes for the blog post

Slug suggestion: `inferhub-client-go-retrieval` — not yet created (connector is insert-only,
`list_posts` first). EN visible / BG hidden, same as every prior client post.

Angle: **the shape question, not the wire question.** Python and TypeScript each had a language
feature to reach for when retrieval needed to be optional-but-call-scoped (keyword-only args,
optional positional args); Go has neither, and a required third parameter would have broken every
`v0.1.0` caller the day this release shipped. The variadic-trailing-parameter answer is worth
naming explicitly as the general pattern for "this needs to stay additive and Go has no default
arguments" — it'll recur at `v1.0.0` and in phase 24's node/admin surface too.

Second thing worth keeping: the two behavioral invariants that ported over unmodified from
Python/TypeScript (partial ingest is data not a failure; search order is never re-sorted) are now
proven in a third language via the *same* conformance cases, not three separate manual QA passes —
this is the corpus doing exactly the job phase 15 built it for.
