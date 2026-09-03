# Social copy — dotnet/v1.6.0 (Iliya posts these manually)

Verify at posting time, not from memory: that 1.6.0 is on nuget.org, and that
`dotnet/samples/FleetOps` runs against the coordinator you point it at. The published package was
driven against a real coordinator after this release shipped (profiles, the model matrix, usage,
clients) — but the model-lifecycle calls (pull/delete/warm) were deliberately not run against that
live fleet, since doing so would touch a real GPU node's disk. Keep that distinction if the copy
claims verification at all.

## Facebook

> InferHub.Client 1.6.0 is out — the C# client catches up with everything the coordinator's admin
> plane grew since this library was 1.0: node profiles, model lifecycle, the fleet-wide model
> matrix, usage accounting, and the configured-clients view. Eleven new methods on the interface
> that was already there — nothing new to register.
>
> The detail worth a post rather than a changelog line: **`PutProfileAsync` sends a name and a
> revision, and the hub ignores both.** We read the coordinator's own source rather than guess —
> `ProfileRegistry.Put` overwrites whatever a client sent with the route segment and its own
> monotonic counter, unconditionally. So this client does not pretend those two fields matter on
> the way in: write a profile, and read back the name and revision the hub actually gave it.
>
> The other one: **`EnsureModelAsync` does not collapse to a boolean.** Asking a fleet to hold a
> model on two nodes can succeed, partially succeed, or fail for four different reasons — cordoned
> nodes, a backend that cannot manage models, an already-satisfied target. The hub's answer names
> all of it: which nodes already had the model, which ones a pull was just sent to, and — when it
> could not fully satisfy the ask — the shortfall and why. A client that reduced that to
> `bool Satisfied` would throw away the only part an operator escalates on.
>
> And usage stays what it has always been here: counts, never content. `QueryUsageAsync` answers
> requests and token totals per client and model — nothing the ledger could not have leaked even if
> asked, because it was never given anything more than a number to hold.
>
> Additive: nothing from 1.5 changed. 246 tests per target framework (243 pass, 3 gated skips), two
> dependencies, still AOT-clean.
>
> Package: nuget.org/packages/InferHub.Client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**~268 characters.** Keep any edit under 280, counting the URL as 23 whatever its real length.

> InferHub.Client 1.6.0 — admin catch-up: node profiles, model lifecycle, usage, clients.
>
> PutProfileAsync sends a name and revision. The hub ignores both — verified against its own
> source, not guessed. Write a profile, read back what it actually gave it.
>
> nuget.org/packages/InferHub.Client

Alternative, ~274, if the "why, not a bool" beat lands better with your audience:

> InferHub.Client 1.6.0 is out.
>
> EnsureModelAsync doesn't return true/false. It returns which nodes already had the model, which
> got a pull, and — if it couldn't fully satisfy the ask — the shortfall and why.
>
> nuget.org/packages/InferHub.Client

## Notes for the blog post

Slug: `inferhub-client-two-fields-the-hub-ignores`. `list_posts` first, then create it **visible in
one shot** — the connector is insert-only with a locking slug, so a draft you meant to fix is a
post you cannot fix. **No shell commands in the HTML**: the Cloudflare WAF blocks the *request*, not
the command, so show the C# and the JSON rather than a `curl` or a `dotnet add package` line inside
a `<pre>`. The post lands at
`blog.devart.solutions/blog/inferhub-client-two-fields-the-hub-ignores`.

Angle: **a client library's job includes knowing which parts of a request the server will simply
overwrite, and saying so rather than pretending they matter.** "We added admin methods" is a
changelog line. The claim worth a post is narrower and more useful: two fields on `NodeProfile`
exist on the wire and do nothing on the way in, and the only way to know that safely is to read the
server's own code rather than infer it from a 200 response that happens to look right either way.

Four things to include:

- **The name and the revision.** Quote `ProfileRegistry.Put`'s three lines: `trimmed`, the
  `AddOrUpdate` counter, and `definition with { Name = trimmed, Revision = revision, ... }`. Say
  plainly that this was found by reading the coordinator's source in this same working session, not
  by testing against a live one — and that the alternative (two DTOs, one for writing and one for
  reading) was considered and rejected because the eleven other fields are identical and a caller
  reading a profile back to edit it would need a mapper for no protocol reason.
- **`EnsureModelResult`'s full shape**, above. Worth naming that `nonManageableHolders` is a real
  and different thing from `eligibleCandidates` — a node running a vLLM upstream can hold a model
  and never be pulled onto, and the two lists are how an operator tells "already fine" from "cannot
  help further" apart.
- **`ClientRow` has no key field**, on purpose, not merely undocumented. `ClientConfig.Key` never
  serializes onto `GET /api/admin/clients` at the hub, so this client's model has nowhere to put one
  even if it wanted to — the absence is structural, not a redaction.
- **`UsageRow` models the wire, not the hub's richer internal type.** The hub's `UsageAggregate`
  carries audio seconds, characters, megapixel-steps and video seconds; `/api/admin/usage`'s public
  projection today exposes only token and request counts. Say this is recorded as a known gap
  rather than an omission — a future release finding the route grown a breakdown field should treat
  it as new, additive surface, not a bug in this one.

**The honest line, and it belongs in the post rather than a footnote.** Every recorded payload in
this phase's tests came from reading the coordinator's endpoint source directly — not from driving
a live coordinator with an admin key. That is a different, weaker kind of evidence than every
earlier phase's "recorded from a real hub" standard, and the post should say so rather than imply
otherwise. No claim about a real fleet's usage numbers, a real profile's convergence time, or a real
ensure's placement decision belongs in this post — nothing here watched one happen.
