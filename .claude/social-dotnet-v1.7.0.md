# Social copy — dotnet/v1.7.0 (Iliya posts these manually)

Verify at posting time, not from memory: that 1.7.0 is on nuget.org, and that `dotnet/samples/NodeTarget`
runs against whatever you point it at. **Unlike every recent phase, nothing here was verified against
a live target this session** — no coordinator or solo node was reachable. Every recorded body in the
tests was reconstructed from reading the hub's own endpoint source (`LocalStatusEndpoints.cs`,
`LocalCollectionEndpoints.cs`, `LocalApiEndpoints.cs`), not captured from a real response. If you can
run `NodeTarget` against a real hub and a real solo node before posting, do that first and fold the
result in — a `ProbeAsync` that returns the right `Kind` against both is the strongest claim this
release can make and it has not been made yet.

## Facebook

> InferHub.Client 1.7.0 is out, and it's the last stop on this client's catch-up with the hub: **a
> node is a base address, not a second client.** A solo InferHub node already answered chat,
> generate, streaming, embeddings, vectors, RAG, the OpenAI dialect, audio, images, video and
> ingestion exactly like a coordinator does — this release adds the one thing it couldn't do before:
> tell you which one you're talking to.
>
> `ProbeAsync()` reads `/api/status` once. A node's document always carries `mode: "solo"`; a
> coordinator's carries no `mode` field at all — that's the whole signal, and it's honest because
> it's the server telling you what it is, not a guess based on which URL you typed.
>
> Two routes exist only on a node — `/api/version` and a `/api/collections` lifecycle, since a solo
> node has no admin plane to gate collection management behind. Call either against a coordinator and
> you get a plain 404, which is correct: the route genuinely isn't there.
>
> And we caught our own mistake while writing this: our docs said a vendor-typed node refuses an
> unsupported embed with a 503. Reading the hub's source said otherwise — a backend that structurally
> can't do it (no Anthropic embeddings API) answers 501, permanently; a capability an operator turned
> off answers 503 with a Retry-After, temporarily. Two different refusals, and we'd been saying one.
> Fixed in the same release that found it.
>
> Additive, as always: nothing from 1.6 changed. 259 tests per target framework (256 pass, 3 gated
> skips), two dependencies, still AOT-clean.
>
> Package: nuget.org/packages/InferHub.Client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**~272 characters.** Keep any edit under 280, counting the URL as 23 whatever its real length.

> InferHub.Client 1.7.0 — a node is a base address, not a second client.
>
> ProbeAsync() reads /api/status once and tells you: coordinator, or solo node. Two node-only routes,
> and a docs mistake we caught ourselves (501 vs 503 aren't the same refusal).
>
> nuget.org/packages/InferHub.Client

Alternative, ~261, if the self-correction is the stronger hook:

> InferHub.Client 1.7.0 is out.
>
> We said a vendor-typed node's missing capability was a 503. Reading the hub's own source said: no,
> a structural refusal is 501, permanent — 503 is only for what an operator switched off. Fixed.
>
> nuget.org/packages/InferHub.Client

## Notes for the blog post

Slug candidate: `inferhub-client-a-node-is-a-base-address`. `list_posts` first, then create it
**visible in one shot** — the connector is insert-only with a locking slug. **No shell commands in
the HTML** — show the C# and the JSON, never a `curl` or `dotnet add package` line inside a `<pre>`.
Lands at `blog.devart.solutions/blog/inferhub-client-a-node-is-a-base-address`.

Angle: **the interesting design decision is what this release does *not* add** — no `IInferHubNodeClient`,
no capability cache, no admin-plane detection beyond the 404 it already is. The whole value is one
new method (`ProbeAsync`) plus four small node-only routes, because the hard work (making a node
serve the same client-facing surface as a coordinator) was already done, seven phases ago.

Four things to include:

- **`ProbeAsync`'s discriminator, and why it isn't the base address.** A caller cannot tell
  `http://localhost:5080` from `http://gpu-box:11434` — both are just a URL. The only honest signal
  is the response body: `mode: "solo"` present or absent. Worth naming the rejected alternative — a
  `HEAD` probe against `/api/version` — and why it fails: a 404 from a wrong address and a 404 from a
  route that just isn't there look identical, and `/api/status` (200 either way) doesn't have that
  ambiguity.
- **The corrected `CLAUDE.md` rule**, told straight: this project's own internal guidance said "503"
  for a vendor-typed refusal, and reading the hub's `LocalApiEndpoints.BackendCannot` vs.
  `CapabilityDisabled` showed two different HTTP statuses for two different conditions — one
  permanent, one temporary. Say plainly that `InferHubException` already modelled both
  (`StatusCode` + `RetryAfter`) and no new exception type was needed; only the claim was wrong.
- **Why there's no `IInferHubNodeClient`.** It would duplicate every method on the existing interface
  to express one fact — that four of them are absent on whichever target this isn't — and it turns
  "same code, laptop or fleet" into a compile-time choice for what's actually a config value.
- **What's honestly missing**: no live coordinator or solo node was reachable to verify `ProbeAsync`
  against a real target of either kind this session. Say so plainly — every recorded test body came
  from reading the hub's endpoint source, not from a captured response, which is a different and
  weaker kind of evidence than most of this track's earlier phases.
