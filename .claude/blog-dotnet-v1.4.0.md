# Blog post — dotnet/v1.4.0, as published

**Posted 2026-09-02**, live at
<https://blog.devart.solutions/blog/inferhub-client-video-and-the-methods-we-did-not-ship> (id
`6a974cc76054b0cf94d3308d`), EN visible / BG hidden, created in one shot after `list_posts`
confirmed the slug was free. Verified `200`.

The `devart` and `devart.solutions` MCP servers failed to connect again at the start of this session
with `Missing sessionId parameter` — the recoverable failure mode — and the
`claude_ai_devart_solutions` connector was working when the post was due, as it was for 1.3.0.
`list_posts` first, then create visible: the connector is insert-only with a locking slug, so a draft
you meant to fix is a post you cannot fix.

No shell command appears in the HTML — the Cloudflare WAF in front of the blog blocks the *request*,
not the command — so the install line is prose and the code blocks are C# and JSON only.

---

- **slug**: `inferhub-client-video-and-the-methods-we-did-not-ship`
- **title (en)**: InferHub.Client 1.4.0: the two methods we did not ship
- **author**: Iliya Nedelchev
- **excerpt (en)**: The C# client can now ask an InferHub fleet for video, which completes every
  modality the hub has. The part worth writing about is what is deliberately absent: two routes of
  OpenAI's Videos API that this hub refuses on purpose, and why shipping methods for them would have
  told callers the wrong story.

## Angle

**The two methods we did not ship.** "We added video" is a changelog line; the interesting claim is
that an SDK's job includes *refusing* to publish a method for a route the server declines by design
— because a member that can only throw reads as "not implemented yet", and because a `1.x` contract
means anything published is kept forever. Both hub sentences are quoted, both recorded.

Then, in order: one record described by two documents, and why we ship both types rather than
mapping either onto the other; the watch being a poll because there is no SSE here, and the 99-cap
it exists to carry; the video grid being 16 where the image grid is 8, with `1920x1080` refused and
`1920x1088` accepted, both recorded; the read-once fetch inheriting 1.3.0's never-retry marker.

## The honest section, as published

It is in the post rather than in a footnote: **no clip was rendered.** The hub used for verification
has one node serving chat and embed with no tool runtime, so it provides no `video` capability. Every
refusal in the suite is recorded from that real hub; every success shape is derived from the hub's
own serializers and marked as derived. The post claims no render times, shows no sample clip, and
says nothing about how the output looks — and it names what *was* established from the published
package against the live hub: the request shapes, the whole failure path, the hub reading a header
this client wrote, and the invariant-culture formatting proved under a `bg-BG` thread culture.
