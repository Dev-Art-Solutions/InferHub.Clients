# Social copy — js/v0.2.0 (Iliya posts these manually)

## Facebook

> inferhub-client 0.2.0 is on npm — retrieval for the TypeScript client. The vector data-plane
> (upsert/query/retrieve), the X-InferHub-Retrieve* RAG headers, and ingestion + search, ported
> from the same plane the Python client shipped in its own v0.2.0.
>
> RetrievalOptions is a second, optional argument to chat/generate rather than a body field —
> TypeScript has no keyword arguments, so this is the idiomatic stand-in for the call-scoped
> keyword Python's client already used. Ingestion and search use FormData for multipart — the
> platform's own encoder, zero new dependency, same as v0.1.0's zero-dependency budget.
>
> Two shapes worth knowing about if you're building on this: a partial ingest comes back as data,
> not an exception — the hub answers a half-embedded document with a 500 that still carries the
> document id and the chunks that landed, and this client hands that back rather than discarding
> it. And search on a collection that doesn't exist throws, unlike a missing document (which comes
> back as undefined) — answering "no hits" for a misspelled collection name would report an empty
> corpus as a working one.
>
> Verified against a real, running coordinator: chat, and chat with retrieval raising the hub's
> own 424 — then verified again from npm install against the public registry, both the import and
> require builds resolving.
>
> js/v1.0.0 closes the surface next: audio, images, admin and probe().
>
> Package: npmjs.com/package/inferhub-client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**Keep under 280 characters total, URL included.**

> inferhub-client 0.2.0 is on npm — retrieval for the TypeScript InferHub client. Vector CRUD, RAG
> headers, ingestion + search over FormData, zero new deps. A partial ingest returns as data, not
> an exception. Verified live and from the published package.
>
> npmjs.com/package/inferhub-client

(~245 characters plus the URL — check with `wc -m` before posting.)

## Notes for the blog post

Slug suggestion: `inferhub-client-typescript-retrieval` — not yet created (connector is
insert-only, `list_posts` first). EN visible / BG hidden, same as every prior client post.

Angle: **the second-language argument now has a rhyme.** Python earned `RetrievalOptions` as a
keyword-only argument because Python has keyword arguments; TypeScript doesn't, so the same
call-scoped idea — "retrieval applies to two different request shapes and shouldn't live in
either one's serializer" — becomes a second positional parameter instead. Same design decision,
translated rather than re-argued, which is exactly the payoff the conformance corpus and the
phase-15/D3 three-phases-per-language shape were bought for.

Three things worth keeping if this gets expanded later:

- **`FormData` over a multipart library, again** — same zero-runtime-dependency argument v0.1.0
  made for `fetch`/`ReadableStream`, now covering file upload too. No dependency was added for
  this release.
- **A `500` that carries a document id is not a failure** — `ingestText`/`ingestFile` check the
  body's shape (`documentId` + `status`) before falling back to throwing, so a half-embedded
  ingest hands back what actually landed instead of losing it to a generic exception handler.
- **The verification gap is the same one Python already named** — this machine's own corpus
  provider is stopped, so `ingestText`/`search` answer a real `404` regardless of client
  correctness (confirmed with `curl` first). The chat + retrieval-header path, including a live
  `424`, was verified end to end; say the gap plainly rather than let a green suite imply more.
