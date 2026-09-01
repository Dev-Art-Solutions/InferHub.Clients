# Social copy — dotnet/v1.4.0 (Iliya posts these manually)

Verify the facts at posting time, not from memory: that 1.4.0 is on nuget.org, that the parity
table's Video row reads ✓ for C#, and that `dotnet/samples/VideoClip` runs against the hub you point
it at. **One fact this release cannot claim** — see the last section — so do not let the copy drift
into "we made a video with it".

## Facebook

> InferHub.Client 1.4.0 is out, and the C# client can ask for video — which completes every
> modality the hub has: text, embeddings, audio, images, and now clips.
>
> `IInferHubVideoClient` covers OpenAI's own Videos API — create, poll, fetch the bytes, delete —
> plus the one route that dialect does not have: the job listing. Same hub, same address, same key
> as the other four surfaces.
>
> The part worth telling is what we deliberately did *not* ship. Two routes of that dialect are
> refused by the hub on purpose: you cannot list videos, because a video id is itself the capability
> to fetch the bytes, and you cannot remix one, because nothing durable holds the prompt that made
> it — no prompt, no negative prompt, by design. Both answer 501 with the reason in the sentence. We
> could have shipped `RemixAsync` and had it throw. We didn't: a published method that can only
> throw reads as "not implemented yet", which is the opposite of what the refusal says — and once a
> method is published in a 1.x line, it has to be kept forever. So the client teaches the refusal
> instead: the error code, the alternative, and the hub's own sentence recorded in the test suite.
>
> Two smaller ones. Video does not stream its progress — the image job seam does, this one doesn't —
> so the watch is a poll, written once in the library rather than in everybody's code, and it carries
> the thing you cannot guess: the hub caps progress at 99 until the render is actually over, so a
> caller who stops at 100 stops one round trip before the bytes exist. And the grids differ: a video
> pipeline downsamples by 16 where an image pipeline downsamples by 8, so 1920x1080 — a perfectly
> good picture — is a 400 for a clip. 1920x1088 is its honest neighbour, and it is a constant in the
> package so nobody learns it from an error message.
>
> Additive: nothing from 1.3 changed. 197 tests per target framework, two dependencies, still
> AOT-clean — and no media library anywhere in it, because nothing here decodes a frame.
>
> Package: nuget.org/packages/InferHub.Client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**~272 characters.** Keep any edit under 280, counting the URL as 23 whatever its real length.

> InferHub.Client 1.4.0 — video, in OpenAI's own async dialect.
>
> Two of that API's routes are 501 by design, so we shipped neither method. A published method that
> can only throw reads as "not implemented yet" — and 1.x means you keep it forever.
>
> nuget.org/packages/InferHub.Client

Alternative, ~268, if the grid beat lands better with your audience:

> InferHub.Client 1.4.0 — video, and every modality the hub has is now reachable from C#.
>
> A video pipeline downsamples by 16 where an image one downsamples by 8. So 1920x1080 is a fine
> picture and a 400 for a clip. That constant now ships in the package.
>
> nuget.org/packages/InferHub.Client

## Notes for the blog post

Slug: `inferhub-client-video-and-the-methods-we-did-not-ship`. `list_posts` first, then create it
**visible in one shot** — the connector is insert-only with a locking slug, so a draft you meant to
fix is a post you cannot fix. **No shell commands in the HTML**: the Cloudflare WAF blocks the
*request*, not the command, so show the C# and the JSON rather than a `curl` or a
`dotnet add package` line inside a `<pre>`. The post lands at
`blog.devart.solutions/blog/inferhub-client-video-and-the-methods-we-did-not-ship`.

Angle: **the two methods we did not ship**. "We added video" is a changelog line. The interesting
claim is that a client's job includes *not* publishing a method for a route the server refuses by
design — because in a 1.x contract, publishing it means keeping it forever, and a member that only
throws tells the caller the wrong story about whose limitation it is. Four good details:

- **The 501s, above.** The hub's own sentences are worth quoting: "a video id is itself the
  capability to fetch the bytes" and "nothing durable holds the request that made a video". Both
  recorded from a live hub.
- **One record, two documents.** The hub runs images and video through one job registry and
  describes that record two ways — OpenAI's `video` object (`video_…` id, a status word, an integer
  progress, unix timestamps) and the job document (bare GUID, a state, step counts). We ship both
  types rather than mapping one onto the other, because the mapping would have to invent values the
  hub never sent. `VideoIdentifier` crosses between the ids so a caller does not learn the prefix
  from a 404.
- **Progress capped at 99.** The hub does it deliberately; the client's watch loop is where that
  fact gets written down once instead of in every caller's polling loop. Also: there is no SSE here,
  and a video id on the *images* events route is a 404, because those routes are capability-scoped.
- **16, not 8.** The grid difference, with 1920x1080 refused and 1920x1088 accepted — both recorded
  from the live hub, which is the sort of thing no schema tells you.

**The honest line, and it belongs in the post rather than in a footnote:** the hub used for
verification has one node serving chat and embed with no tool runtime, so there is no `video`
capability on it. Every refusal in the suite is recorded from that real hub — eleven of them,
including both 501s — and the success shapes are derived from the hub's own serializers and marked
as derived. **No clip was rendered.** Nothing in the post should claim otherwise: no render times,
no sample video, no "the 5-second default looks good". What *was* established end to end is the
request shape and the whole failure path, from the published package against the real hub —
including the real hub reading an `X-InferHub-Video-Steps` header this client wrote.
