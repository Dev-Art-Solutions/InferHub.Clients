# Social copy — js/v0.1.0 (Iliya posts these manually)

## Facebook

> inferhub-client is now on npm — the third client in the InferHub.Clients repo, and the first
> proof that the shared conformance corpus pays off for a runtime shaped nothing like the first
> two. v0.1.0 covers the core surface: chat, generate (blocking and streaming), embeddings, model
> listing, status and health.
>
> Python needed two client classes because Python has two incompatible calling conventions and a
> sync-over-asyncio.run shim breaks inside code already running an event loop. TypeScript only has
> one async convention, so there's one class: blocking calls return a Promise, streams return an
> AsyncIterable. Zero runtime dependencies — fetch and ReadableStream are the platform on Node 18+,
> every evergreen browser, Deno and Bun — and it ships as both ESM and CJS from one build, so
> import and require both just work.
>
> Verified against a real, running coordinator: chat, streaming chat, embeddings, model listing,
> status, and a real 404 matching the recorded error message byte for byte — then verified again
> from npm install against the public registry, in a clean scaffold outside the repo, both builds
> resolving.
>
> Retrieval lands in v0.2.0, the rest of the surface (audio, images, admin, the node) in v1.0.0 —
> same three-release shape the Python client already proved.
>
> Package: npmjs.com/package/inferhub-client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**Keep under 280 characters total, URL included.**

> inferhub-client 0.1.0 is on npm — TypeScript client for InferHub. Chat, generate, streaming,
> embeddings. One class, zero runtime deps, fetch/ReadableStream only, ESM+CJS. Verified live and
> from the published package.
>
> npmjs.com/package/inferhub-client

(~230 characters plus the URL — check with `wc -m` before posting.)

## Notes for the blog post

Slug suggestion: `inferhub-client-typescript-core` — not yet created (connector is insert-only,
`list_posts` first). EN visible / BG hidden, same as every prior client post.

Angle: **the corpus keeps paying off, and this time in a runtime that shares nothing with the
first two.** C# and Python are both structurally typed, exception-based, single calling
convention (well, Python has two — see below). TypeScript's one-class design (D1 in
`plans/phase-19-js-core.md`) is the sharpest illustration that "idiomatic per ecosystem" (D4 of
the polyglot roadmap) is a real constraint, not a checkbox: Python earned two classes from a real
runtime hazard (the asyncio.run shim breaking inside an existing event loop); TypeScript's
Promise/AsyncIterable split needed none of that ceremony because JavaScript only has the one async
model to begin with.

Three things worth keeping if this gets expanded later:

- **Zero runtime dependencies, including no SSE library** — the hub's streaming responses are
  NDJSON, never actual `text/event-stream`, so there's no SSE parser to depend on in the first
  place; the shared line-buffering async generator is ~50 lines the corpus already exercises.
- **Dual ESM/CJS from one source tree, not two hand-written builds** — `tsup` (dev dependency
  only) emits both; the `exports` map in `package.json` is what routes `import` vs `require` to
  the right file, confirmed by building a scratch scaffold outside the repo and loading both ways.
- **The verification gap, if any, should be named plainly** — this release ran every done-criteria
  call against a real coordinator, so there isn't one to hide this time; say that plainly too,
  it's the exception rather than the rule across this track's release notes.
