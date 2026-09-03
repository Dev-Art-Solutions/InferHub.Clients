# Blog post — dotnet/v1.6.0, PARKED (devart MCP connector failed to connect, not the recoverable "Missing sessionId" blip)

**Not yet posted.** Both `devart` and `devart.solutions` MCP servers refused the connection this
session (`SdkHttpError dialing https://blog.devart.solutions/api/mcp?apiKey=REDACTED
(CLIENT_HTTP_NOT_IMPLEMENTED)`) — a connection failure, not the transient `Missing sessionId`
blip this repo has recovered from before by waiting it out. When the connector is reachable again:
`list_posts` first to confirm the slug is free, then **one** `create_post`, visible in both fields —
the connector is insert-only with a locking slug, so a draft that goes out wrong cannot be fixed,
only replaced under a new slug.

- **slug**: `inferhub-client-two-fields-the-hub-ignores`
- **title (en)**: InferHub.Client 1.6.0: two fields the hub ignores
- **author**: Iliya Nedelchev
- **excerpt (en)**: The C# client now covers the coordinator's whole admin plane — node profiles,
  model lifecycle, usage, clients. The part worth writing about is what we found reading the hub's
  own source: two fields a client sends that the hub throws away on purpose, and why modelling that
  honestly beat modelling it politely.

No shell command in the HTML — the Cloudflare WAF in front of the blog blocks the *request*, not
the command — so the install line is prose and the code blocks are C# and JSON only.

---

## HTML body (paste as the post content, entity-escaped as the connector expects)

<p>InferHub.Client 1.6.0 is out. The C# client now covers everything the coordinator's admin plane
grew since this library shipped its first release against coordinator v2.x: node profiles, model
lifecycle, the fleet-wide model matrix, usage accounting, and the configured-clients view. Eleven
new methods on the interface that was already there &mdash; nothing new to register.</p>

<p>The detail worth a post rather than a changelog line is not a feature. It is something we found
by reading the coordinator's own source rather than guessing: <strong>writing a node profile sends
a name and a revision, and the hub ignores both.</strong></p>

<pre><code>var written = await admin.PutProfileAsync("gpu-nodes", new NodeProfile
{
    Selector = new NodeProfileSelector { Labels = new Dictionary&lt;string, string&gt; { ["gpu"] = "true" } },
    MaxConcurrency = 4
});

Console.WriteLine($"'{written.Profile.Name}' rev {written.Profile.Revision}");
// prints the hub's own name and revision, not whatever this object held before the call
</code></pre>

<p>The coordinator's <code>ProfileRegistry.Put</code> is three lines once you strip the logging:</p>

<pre><code>var trimmed = name.Trim();
var revision = revisions.AddOrUpdate(trimmed, 1, (_, current) =&gt; current + 1);
var stored = definition with { Name = trimmed, Revision = revision, Selector = definition.Selector ?? new NodeProfileSelector() };
</code></pre>

<p>Whatever a caller put in <code>Name</code> and <code>Revision</code> is overwritten, unconditionally,
with the route segment and the hub's own monotonic counter. A client library has two honest choices
here: model two types &mdash; one for writing, with no name or revision field to fill in and get wrong,
one for reading, with both &mdash; or model one type and say plainly, in the one place a caller will
read it, that these two fields do nothing on the way in. We took the second, because the other nine
fields on a profile are identical in both directions, and a caller who reads a profile back to edit
it would need a mapper between two types for no protocol reason. The cost is one paragraph of XML
doc comment instead of a compiler that would have caught the mistake for you. We think that is the
right trade for a field the server was always going to overwrite in the first place &mdash; but it is
a trade, and we would rather say so than have someone find it by watching a name silently not take.</p>

<p>The second thing worth a post: <strong><code>EnsureModelAsync</code> does not collapse to a
boolean.</strong> Asking a fleet to hold a model on two nodes can succeed outright, partially
succeed, or fail for reasons that have nothing to do with each other &mdash; a cordoned node, a
backend that cannot manage models at all, a target that was already satisfied before the call did
anything. The hub's answer names all of it:</p>

<pre><code>var ensured = await admin.EnsureModelAsync("llama3", replicas: 2);

if (!ensured.Satisfied)
{
    Console.WriteLine(ensured.Decision.Note);                                    // why, in words
    Console.WriteLine($"short by {ensured.Decision.Shortfall}");                 // how much
    Console.WriteLine(string.Join(", ", ensured.Decision.NonManageableHolders)); // holds it, can't be pulled onto again
}
</code></pre>

<p><code>nonManageableHolders</code> and <code>eligibleCandidates</code> are two different lists for a
reason: a node running a vLLM upstream can hold a model already and never be a target for a pull,
and an operator staring at "not satisfied" needs to know whether that is "nothing more to do" or
"here is a real gap." A client that reduced this to <code>bool Satisfied</code> would have thrown
away the only part of the answer worth escalating on.</p>

<p>Two smaller ones, in the same spirit. <code>ListClientsAsync</code> has no field for a client's
key &mdash; not blanked out, not marked sensitive, just structurally absent, because the hub's own
response never carries one to begin with. And <code>QueryUsageAsync</code>'s row type models what
the wire actually sends today &mdash; requests and token counts &mdash; rather than the hub's richer
internal aggregate, which also tracks audio seconds, characters and image/video units on the
server side. Those four totals are not serialized onto this route yet. We wrote that down rather
than guess at a shape the hub might grow into, so a future release that finds them there knows to
treat it as new surface, not a bug in this one.</p>

<p>The published package was installed from nuget.org and driven against a real, running coordinator
with a real connected node: the profile round trip, the model matrix, real historical usage rows,
the configured-clients view, and a node with no profile applied all came back exactly as modelled.
<strong>One honest line rather than a blanket claim:</strong> the model-lifecycle calls &mdash;
pulling, deleting or warming a model, on a node or on a tool's catalogue &mdash; were not run
against that live fleet, because doing so would have pulled, deleted or warmed something on a real
GPU box, which is not a documentation check's call to make. Those five stay verified against the
coordinator's own source and the test suite's recorded payloads only.</p>

<p>Additive, as every release here has been: nothing from 1.5 changed, and a caller compiled against
it keeps compiling. 246 tests across both target frameworks, two dependencies, still AOT-clean.</p>

<p>Package: <a href="https://www.nuget.org/packages/InferHub.Client/">nuget.org/packages/InferHub.Client</a><br>
Code: <a href="https://github.com/Dev-Art-Solutions/InferHub.Clients">github.com/Dev-Art-Solutions/InferHub.Clients</a></p>
