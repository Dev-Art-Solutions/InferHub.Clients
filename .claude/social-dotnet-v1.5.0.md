# Social copy — dotnet/v1.5.0 (Iliya posts these manually)

Verify the facts at posting time, not from memory: that 1.5.0 is on nuget.org, that the parity
table's ingestion row reads ✓ for C#, and that `dotnet/samples/Ingest` runs against the target you
point it at. **Two facts this release cannot claim** — see the last section — so do not let the copy
drift into "we ran it against a hub with a full corpus".

## Facebook

> InferHub.Client 1.5.0 is out. The C# client could already *query* a vector collection; now it can
> fill one. Upload a document — text, Markdown, HTML, JSON, PDF — and the hub extracts it, chunks
> it, embeds the chunks on your GPU fleet and writes them away. Then list what is in a collection,
> read the chunks a document actually became, delete it, and search it in vector, keyword or hybrid
> mode with optional reranking.
>
> Two things in this release are worth more than the feature list, because both are places where the
> obvious client code is wrong.
>
> **The first: a partially ingested document comes back as an HTTP 500 — and we return it instead of
> throwing.** The hub answers an error status on purpose, because a half-ingested document that
> claims success is worse than a failure. But the body is complete: it names the document, says how
> many chunks embedded and how many did not, and explains why. The chunks that landed are really in
> the store, and re-posting the same bytes resumes rather than duplicating. The reflex every one of
> us has — 5xx means throw — would have discarded the one thing the caller needs to recover: the id.
>
> **The second: a reranked search comes back in an order its own scores contradict.** Reranking sorts
> the candidates by what a chat model said about them and leaves each retrieval score exactly as it
> was. So a real answer we recorded starts with a hit scoring 0.0164 sitting above one scoring
> 0.0325 — and it is right, because the question was about expense approval and the higher-scoring
> chunk was about an unrelated error code. A client that helpfully sorted by score would silently
> undo the rerank its caller asked for and paid a round trip for. So the hits are handed over exactly
> as they arrived, and the score is documented as what retrieval scored — not what ranked it.
>
> A smaller one with teeth: on a multipart upload every form field is written before the file part.
> Above a certain size the hub routes the request from its leading fields while your bytes are still
> arriving, so a field written after the file is refused. Below that size any order works — which is
> exactly what makes the mistake dangerous: correct on every small test file, wrong on the first real
> one, in production. There is a test that reads the produced request body and checks the order.
>
> Additive: nothing from 1.4 changed. 227 tests per target framework, two dependencies, still
> AOT-clean.
>
> Package: nuget.org/packages/InferHub.Client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**~271 characters.** Keep any edit under 280, counting the URL as 23 whatever its real length.

> InferHub.Client 1.5.0 — document ingestion and search.
>
> A reranked answer is not in score order: the reranker sorts by relevance and leaves the scores
> alone. Sort by score to be tidy and you undo the rerank you paid for.
>
> nuget.org/packages/InferHub.Client

Alternative, ~276, if the 500 beat lands better with your audience:

> InferHub.Client 1.5.0 — documents in, chunks out.
>
> A partial ingest arrives as an HTTP 500 with a complete body, so we return it rather than throw.
> "5xx means throw" would discard the document id you need to resume.
>
> nuget.org/packages/InferHub.Client

## Notes for the blog post

Slug: `inferhub-client-the-500-you-should-read-and-the-order-you-should-not-fix`. `list_posts`
first, then create it **visible in one shot** — the connector is insert-only with a locking slug, so
a draft you meant to fix is a post you cannot fix. **No shell commands in the HTML**: the Cloudflare
WAF blocks the *request*, not the command, so show the C# and the JSON rather than a `curl` or a
`dotnet add package` line inside a `<pre>`. The post lands at
`blog.devart.solutions/blog/inferhub-client-the-500-you-should-read-and-the-order-you-should-not-fix`.

Angle: **two places where the helpful client is the wrong client.** "We added ingestion" is a
changelog line. The claim worth a post is that a client library's job is to hand over what the
server actually said — including an error status that carries an outcome, and an order the server
chose for a reason — and that the instincts which make a wrapper feel polished are exactly what
destroy the information. Four good details:

- **The 500 that is an answer.** Quote the recorded body. The status code and the payload disagree
  on purpose, and the hub is right on both counts: it is not a success, and the caller still needs
  the id. Note that `chunksEmbedded: 0` is a real case, and that when *every* batch fails there is
  no document at all — so "partial" is a statement about the call, not a promise that something is
  retrievable.
- **The order the scores contradict.** The recorded table — `policy.txt` at 0.0164 above
  `onboarding` at 0.0325 for "how do I get an expense approved" — is the whole argument in two rows.
  Worth adding: nothing in the response says whether reranking ran. With no rerank model resolved,
  on a parse failure or on a timeout, the hub keeps the original order and logs it, so the answer is
  the same shape either way. (We hit that ourselves: a 0.5B model could not produce a parseable
  score array, and the search came back in fused order with no signal at all.)
- **Field order in a multipart body**, above. It is the best kind of trap — invisible in tests,
  size-dependent in production.
- **The file name, sent here and dropped on image uploads.** The same library refuses to send the
  name you gave a picture and insists on the one you gave a document, because the hub reads the
  extension to pick an extractor and stores the name as each chunk's source. Two rules that look
  contradictory and are the same rule: send what the server needs, log neither.

**The honest lines, and they belong in the post rather than in a footnote.** Every payload in the
suite was recorded from a live InferHub 3.37.0 — including the reranked search and the partial 500 —
but from a **standalone node in solo mode**, not from the coordinator: the always-on hub runs with
its vector store off, where these routes are not mapped at all and answer a 404 with an empty body.
That is a fair recording (ingestion, chunking, the document index and the search pipeline are shared
code the node runs unchanged) and it is not the whole thing: PDF extraction and node-owned-collection
dispatch are coordinator-side and untested here. And the partial we recorded is the case where every
batch failed; a genuinely *mixed* one needs a fleet that breaks halfway through a document, and we
did not arrange that. Say both. No claim about corpus sizes, ingest throughput or retrieval quality
belongs in this post — nothing here measured any of them.
