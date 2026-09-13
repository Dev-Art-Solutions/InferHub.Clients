# Social copy — go/v1.0.0 (Iliya posts these manually)

## Facebook

> InferHub.Clients' Go client hits 1.0.0: audio, images (sync and async jobs), the admin plane and
> the node close the surface — the same three-release shape Python and TypeScript already proved.
> This is a semver promise now: additive-only from here.
>
> The node story is Probe() — one GET /api/status, discriminated on whether the body carries a
> "mode" field at all (the hub's own document never does). It returns a typed result a caller
> switches on for hub vs. solo node, and the retrieval rerank field on a node's status is typed as
> a string from day one — that's the conformance corpus's founding case: dotnet's first release
> typed it as a bool and threw the first time it hit a real node with retrieval on. Go's client
> never had the chance to make that mistake, because the test existed before the code did.
>
> No video module, same as every other language here — the hub permanently refuses video listing
> and remix, so nothing gets published that could only throw. A named constant teaches the refusal
> instead of hiding it.
>
> 10 of 13 shared conformance cases now pass; the remaining 3 are an OpenAI chat dialect that stays
> C#-only by design. Still zero dependencies — go.mod has no require block, through three releases.
>
> This closes Go's own three-phase track. Last stop for the whole polyglot effort: a verification
> day that installs every published package, in every language, from its own public registry, and
> drives it against a real coordinator and a real solo node.
>
> Package: pkg.go.dev/github.com/Dev-Art-Solutions/InferHub.Clients/go
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**Keep under 280 characters total, URL included.**

> InferHub.Clients go/v1.0.0: audio, images, admin plane, and the node. Closes the Go client's
> surface — same 3-release shape as Python/TypeScript. Still zero deps. 10/13 conformance cases
> pass. Semver promise from here.
>
> pkg.go.dev/github.com/Dev-Art-Solutions/InferHub.Clients/go

(~250 characters plus the URL — check with `wc -m` before posting.)

## Notes for the blog post

Slug suggestion: `inferhub-client-go-1-0` — not yet created (connector is insert-only,
`list_posts` first). EN visible / BG hidden, same as every prior client post.

Angle: **the corpus caught the exact same bug class a second time, in a different language, before
it happened.** `node-status-rerank-is-a-string` was written because dotnet got this one field wrong
against a real node. Go's client never had the chance to repeat it — the corpus case existed before
the Go type did, so the "wrong type for a field that looks boolean but isn't" mistake was
structurally unavailable this time. That is the entire argument the polyglot track was built on
(phase 15's "guard the guard" rule), landing for real on the fourth language.

Second thing worth keeping: **this release is a genuine milestone, not just another phase** — it is
the third and last language to reach 1.0.0 with the corpus's help, after Python and TypeScript, and
it took exactly three releases each time instead of C#'s original eight. The corpus is what made
the second, third and fourth languages cheap; this is the receipt.
