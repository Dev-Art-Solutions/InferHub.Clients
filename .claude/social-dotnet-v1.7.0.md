# Social copy — dotnet/v1.7.0 + v1.7.1 (Iliya posts these manually)

**Post about `1.7.1`, not `1.7.0`.** `1.7.0` shipped with a bug (`NodeStatusResponse.Retrieval.Rerank`
typed as a bool when the real node sends a string) found by installing it from nuget.org and driving
a real coordinator and a real solo node in Docker — the first live check this track has run. `1.7.1`
is the fix, same day. Verify at posting time that `1.7.1` is the version on nuget.org before copying
the line below.

## Facebook

> InferHub.Client 1.7.0/1.7.1 is out, and it's the last stop on this client's catch-up with the hub:
> **a node is a base address, not a second client.** A solo InferHub node already answered chat,
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
> And the honest part: we shipped `1.7.0` with a real bug in it. The retrieval status block's
> `rerank` field is a string on a real node (`"none"`/`"llm"`) — we'd typed it as a bool, because no
> node was reachable to check against when that phase was written. Installing `1.7.0` from nuget.org
> into a clean directory and driving it against an actual solo node threw a `JsonException` on the
> first call. `1.7.1`, same day, is the one-line fix — and the reason to run the install-from-registry
> step every single time, not just when it's convenient.
>
> Additive otherwise: nothing from 1.6 changed. 259 tests per target framework (256 pass, 3 gated
> skips), two dependencies, still AOT-clean.
>
> Package: nuget.org/packages/InferHub.Client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**~275 characters.** Keep any edit under 280, counting the URL as 23 whatever its real length.

> InferHub.Client 1.7.1 — a node is a base address, not a second client.
>
> ProbeAsync() reads /api/status once and tells you: coordinator, or solo node. Shipped 1.7.0 with a
> real bug (rerank is a string, not a bool) — caught by driving a live node, fixed same day as 1.7.1.
>
> nuget.org/packages/InferHub.Client

Alternative, ~268, if the honest-bug angle is the stronger hook on its own:

> InferHub.Client 1.7.0 shipped with a bug: a node status field we'd typed as bool is really a string
> on a real server. Found it by installing from nuget.org and driving an actual node. Fixed same day,
> 1.7.1.
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

Five things to include:

- **`ProbeAsync`'s discriminator, and why it isn't the base address.** A caller cannot tell
  `http://localhost:5080` from `http://gpu-box:11434` — both are just a URL. The only honest signal
  is the response body: `mode: "solo"` present or absent. Worth naming the rejected alternative — a
  `HEAD` probe against `/api/version` — and why it fails: a 404 from a wrong address and a 404 from a
  route that just isn't there look identical, and `/api/status` (200 either way) doesn't have that
  ambiguity.
- **The corrected `CLAUDE.md` rule**, told straight: this project's own internal guidance said "503"
  for a vendor-typed refusal, and reading the hub's `LocalApiEndpoints.BackendCannot` vs.
  `CapabilityDisabled` showed two different HTTP statuses for two different conditions — one
  permanent, one temporary.
- **Why there's no `IInferHubNodeClient`.** It would duplicate every method on the existing interface
  to express one fact — that four of them are absent on whichever target this isn't — and it turns
  "same code, laptop or fleet" into a compile-time choice for what's actually a config value.
- **The `1.7.0` → `1.7.1` bug, as the headline honesty beat.** Say plainly: the phase brief for 1.7.0
  admitted no live target was reachable to verify `ProbeAsync` against. The very next thing that
  happened — installing the published package and driving a real solo node, which is a mandatory
  release-ritual step, not optional diligence — found a real bug within minutes: `rerank` is a string
  on the wire, not a bool. Immutable packages meant the fix had to be a new version, not an edit; 1.7.1
  is that version, shipped the same day. This is a better story than "we tested it," because it shows
  what the test that matters actually catches.
- **What's still not covered**: this verification used one coordinator with one meshed node and one
  freshly-started solo node container — not a fleet under load, not every capability combination. Say
  so rather than imply exhaustive coverage.
