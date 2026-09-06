# Social copy — python/v0.2.0 (Iliya posts these manually)

## Facebook

> inferhub-client 0.2.0 is on PyPI: the Python client's second release adds **retrieval** — the
> vector data-plane, `X-InferHub-Retrieve*` headers, ingestion and search — the surface the C#
> client earned across three separate releases, done here in one because the shared conformance
> corpus already knew several of the answers.
>
> `retrieval` is a keyword on `chat()`/`generate()`, not a second request type:
>
> ```python
> answer = client.chat(request, retrieval=RetrievalOptions(collection="docs", k=5))
> ```
>
> Asked for and unavailable is HTTP 424, raised as its own exception type — different from a
> missing model. Search hits stay in the hub's own order and are never re-sorted by score, because
> a reranked list routinely has a lower score sitting above a higher one. A partially-landed ingest
> answers HTTP 500 with a real body, and this client returns that result instead of raising —
> throwing it away would lose the document id needed to resume.
>
> The honest part: this release found and fixed a bug in the *previous* one before it ever caused a
> reported problem. The client's default header setup silently broke every multipart upload, caught
> by testing what the HTTP library actually builds before writing the feature that needed it.
>
> Verified against a live coordinator, both before and after installing from the public PyPI index.
> One thing wasn't verified live this round — the demo server's own vector plane is switched off —
> and the notes say so rather than imply otherwise.
>
> Package: pypi.org/project/inferhub-client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**~270 characters.**

> inferhub-client 0.2.0: retrieval for the Python client — vectors, RAG headers, ingestion, search.
> Found a header bug that broke multipart uploads before it shipped, by testing httpx's own request
> building first. Verified live, one gap named honestly (vector plane off on the demo server).
>
> pypi.org/project/inferhub-client

## Notes for the blog post

Slug: `inferhub-client-python-retrieval` — already created, EN visible / BG hidden. Lands at
`blog.devart.solutions/blog/inferhub-client-python-retrieval`.

Angle: **a header default that had been silently wrong since v0.1.0, caught before it shipped a
broken feature** rather than after a user reported one — the discipline this project keeps
asking of the *dotnet* track (verify before publishing) paying off inside Python's own dependency,
`httpx`, this time.

Three things worth keeping if this gets expanded later:

- **Retrieval is a header-builder, not a body field** — `RetrievalOptions` stays off
  `ChatRequest`/`GenerateRequest` on purpose, because folding a header-only concern into the body
  serializer would need it to know to skip that field.
- **`IngestResult` on a 500** is the same rule the C# client already lives by (root `CLAUDE.md` rule
  11): an error status that carries an outcome is read, not thrown.
- **The verification gap is named, not hidden.** The demo coordinator's vector/corpus provider is
  off; turning it on is a hub change, not a client one, so the release notes and the site both say
  "not established" rather than quietly skip the claim.
