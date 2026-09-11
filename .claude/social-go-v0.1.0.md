# Social copy — go/v0.1.0 (Iliya posts these manually)

## Facebook

> The fourth and last language of InferHub.Clients just shipped its first release: a Go client,
> stdlib net/http only, zero dependencies — go.mod has no require block and no go.sum. v0.1.0
> covers the core surface: chat, generate (blocking and streaming), embeddings, model listing,
> status and health.
>
> Go was deliberately last of the four — the idea being that if Go turned out to be the hard one,
> the shared conformance corpus this whole track runs on wasn't actually finished. It was the
> boring one. The one real Go-specific wrinkle was in the error type, not the wire: the natural
> translation of the C#/Python/TypeScript exception hierarchy — embedding a base *Error inside a
> wrapper type — silently breaks Go's own interface satisfaction, because the embedded field's
> implicit name collides with its own promoted Error() method. Ended up with one flat *Error and a
> Kind field instead.
>
> Verified from the published module, not just the working copy: go get straight from the Go
> module proxy off the pushed tag (no separate publish step the way npm/PyPI need one), into a
> clean scratch module, against this project's own running coordinator — status, 29 models listed,
> a real chat reply, a 768-dimension embedding, a 30-chunk streamed response, and a real 404 for an
> unknown model matching the corpus byte for byte.
>
> Retrieval lands in v0.2.0, the rest of the surface (admin, the node) closes it out at v1.0.0 —
> same three-release shape every language in this track has now proved.
>
> Package: pkg.go.dev/github.com/Dev-Art-Solutions/InferHub.Clients/go
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**Keep under 280 characters total, URL included.**

> InferHub.Clients go/v0.1.0 is out — Go client, stdlib net/http only, zero deps. Chat, generate,
> streaming, embeddings. Fourth language, same shared conformance corpus. Verified live and from
> the module proxy.
>
> pkg.go.dev/github.com/Dev-Art-Solutions/InferHub.Clients/go

(~225 characters plus the URL — check with `wc -m` before posting.)

## Notes for the blog post

Slug suggestion: `inferhub-client-go-core` — not yet created (connector is insert-only,
`list_posts` first). EN visible / BG hidden, same as every prior client post.

Angle: **the corpus's last and hardest test wasn't the wire, it was the language.** Every prior
client phase found its interesting cases on the wire (comma-separated `X-InferHub-Sources`, `424`
vs `404`, a rerank that contradicts its own score). Go's core release found nothing new on the
wire — 4 of 13 corpus cases pass unmodified, same as the other languages' first releases — but it
found a real bug in the *shape a client takes*, not in the hub: the C#/Python/TypeScript error
hierarchy (a base exception type, subclasses per envelope) doesn't translate to Go, because
embedding an anonymous `*Error` inside a wrapper collides the field's implicit name with its own
promoted `Error()` method and silently breaks the wrapper's `error` interface. One flat `*Error`
with a `Kind` discriminator instead — worth keeping as the illustration that D4 ("idiomatic per
ecosystem") sometimes means a shape that isn't just cosmetically different, but structurally
impossible to port.

Second thing worth keeping: **this phase was implemented with no Go toolchain in the authoring
environment**, verified afterward by installing one and running the full checklist (build, vet,
gofmt, test) before tagging. Worth naming plainly rather than glossing over, per this track's own
practice of saying what wasn't established rather than implying it away.
