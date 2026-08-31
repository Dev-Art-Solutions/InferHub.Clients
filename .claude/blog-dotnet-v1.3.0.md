# Blog post — dotnet/v1.3.0, as published

**Posted 2026-08-31**, live at
<https://blog.devart.solutions/blog/inferhub-client-images-and-the-job-seam> (id
`6a95de3aa677799991b3b890`), EN visible / BG hidden, created in one shot after `list_posts`
confirmed the slug was free. Verified `200`.

The `devart` and `devart.solutions` MCP servers both failed to connect at the start of this session
with `Missing sessionId parameter` — the recoverable failure mode, not the organization-membership
one — and the `claude_ai_devart_solutions` connector was working by the time the post was due.
`list_posts` first, then create visible, as always: the connector is insert-only with a locking
slug, so a draft you meant to fix is a post you cannot fix.

No shell command appears in the HTML — the Cloudflare WAF in front of the blog blocks the *request*,
not the command — so the install line is prose and the code blocks are C# and JSON only.

---

- **slug**: `inferhub-client-images-and-the-job-seam`
- **title (en)**: InferHub.Client 1.3.0: the retry that would have eaten your picture
- **author**: Iliya Nedelchev
- **excerpt (en)**: The C# client can ask an InferHub fleet for pictures — three synchronous routes
  and an async job seam with per-step progress. The part worth writing about is a generic resilience
  feature we had to switch off, because the content it would retry does not exist twice.

## Angle

**The retry we had to turn off.** "We added images" is a changelog line; the interesting claim is
that read-once content turns a correct, generic resilience feature into a destructive one, and that
the fix is a marker on the request rather than a caveat in a README. The post opens there and comes
back to it: the second test — an ordinary GET with the same options still retries — is what makes
the first one mean anything.

Then, in order: the two ways of asking being one request; `MediaJob` named for the seam rather than
for today's caller; the two variation refusals made unrepresentable by two types; `0,75` as a 400
and the invariant-culture test that pins it; the 503-you-retry versus the 404-you-do-not.

## The honest section, as published

It is in the post rather than in a footnote: **no image was generated.** The hub used for
verification runs one node serving chat and embed with no tool runtime. Fourteen checks from the
published package against the real hub are described, with the three that carry the release named —
the hub reading an `X-InferHub-Image-Steps` header this client wrote, and parsing the multipart edit
and variation forms it builds. Fifteen recorded refusals; the success shapes derived from the hub's
own serializers and marked as derived in the test file.

No render times, no sample images, no claim about seam repair.
