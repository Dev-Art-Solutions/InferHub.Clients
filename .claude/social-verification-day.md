# Social copy — the verification day (phase 25, closes the polyglot-clients track) (Iliya posts these manually)

## Facebook

> The InferHub.Clients polyglot track is closed. Four languages — C#, Python, TypeScript, Go —
> each with a published package, and today all four were installed fresh from their public
> registries (NuGet, PyPI, npm, the Go module proxy) and driven against a real coordinator and a
> real solo node, stood up from source for the occasion. Everything worked, nothing needed a
> patch.
>
> It did find two real inconsistencies — on the server side, not in any client: an unknown model
> answers 404 from a coordinator and 502 from a solo node, and a bare untagged model name resolves
> through a node (which forwards straight to Ollama) but not through the coordinator's exact-tag
> match. Every client surfaced whatever the server actually said, which is the whole point of the
> rule this project has followed since the first release.
>
> Fifteen phases, one shared conformance corpus, four languages that each got cheaper to trust
> because of the one before it — Python and TypeScript reached parity in three releases instead of
> C#'s original eight, and Go matched them. Full record in the repo's own
> .claude/verification-clients.md.
>
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**Keep under 280 characters total, URL included.**

> InferHub.Clients polyglot track closed: all 4 published packages (C#/Python/TS/Go) installed
> fresh from their registries, verified against a real coordinator + real solo node in one
> sitting. Found 2 hub/node quirks, 0 client bugs.
>
> github.com/Dev-Art-Solutions/InferHub.Clients

(~240 characters plus the URL — check with `wc -m` before posting.)

## Notes for the blog post

Already published: `inferhub-clients-verification-day`, EN-visible/BG-hidden.

Angle already used in the post: the two real findings were on the InferHub server side, and the
posture that matters is that every client surfaced them faithfully rather than smoothing them over.
