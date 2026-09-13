# Social copy — combined recap, everything shipped today (2026-09-13): go/v0.2.0, go/v1.0.0, phase 25 (Iliya posts these manually)

## Facebook

> Three releases in one day closed out the InferHub.Clients polyglot-clients track.
>
> go/v0.2.0 gave the Go client retrieval — vector CRUD, RAG headers, ingestion and search. The
> interesting problem wasn't the wire, it was the API shape: Python has keyword-only args,
> TypeScript has optional positional params, Go has neither. Landed on a trailing variadic
> parameter (Chat(ctx, request, retrieval...)) — the one shape that's both idiomatic and stays
> additive over every existing call site.
>
> go/v1.0.0 closed the surface — audio, images, the admin plane, and the node. The node's status
> field for the rerank mode is typed as a string from day one, because the shared conformance
> corpus already had a case for the exact bug dotnet made here a while back (typed it as a bool,
> threw against a real node). The corpus did its job a second time, in a different language, before
> the mistake could repeat.
>
> Then the day that has no code in it: phase 25, the verification day. All four published packages
> — C#, Python, TypeScript, Go — installed fresh from NuGet, PyPI, npm and the Go module proxy, and
> driven against a real coordinator AND a real solo node, both stood up from source for the
> occasion. Everything worked. It did turn up two real inconsistencies on InferHub's own side (an
> unknown model answers 404 from a coordinator and 502 from a solo node; an untagged model name
> resolves on a node but not through the coordinator's exact-tag match) — neither is a client bug,
> and every client surfaced them exactly as the server sent them rather than smoothing them over.
>
> Fifteen phases, four languages, one shared test file that made the third and fourth language
> three releases instead of eight. The whole track is closed now.
>
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**Keep under 280 characters total, URL included.**

> Shipped 3 releases today: go/v0.2.0 (retrieval), go/v1.0.0 (audio/images/admin/node — closes the
> Go client), and the verification day — all 4 published clients (C#/Python/TS/Go) checked against
> a real coordinator + real solo node. Track closed.
>
> github.com/Dev-Art-Solutions/InferHub.Clients

(~250 characters plus the URL — check with `wc -m` before posting.)
