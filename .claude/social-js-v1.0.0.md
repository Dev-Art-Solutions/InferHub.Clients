# Social copy — js/v1.0.0 (Iliya posts these manually)

## Facebook

> inferhub-client 1.0.0 is on npm — audio, images, the admin plane and the node close the
> TypeScript client's surface, the same three-release shape the Python client proved two weeks
> earlier. This is the client's 1.0.0 semver contract now: additive-only from here.
>
> createSpeech/streamSpeech hand back the live Response whether or not you asked for streaming —
> not one line of caller code differs. Images get the sync /v1/images/* routes plus the whole
> async job seam (submit, watch over SSE, read the picture back once — a retry after that is a
> 410). Admin methods live on the same InferHubClient as everything else; construct it with an
> admin key instead of a client key, no second class needed. And probe() tells a coordinator from
> a solo node with one GET /api/status.
>
> No video module, on purpose: the hub permanently 501-refuses video listing and remix, and a
> method that can only throw is not something this client publishes.
>
> Verified against a real, running coordinator — probe(), fleet listing, the model matrix and a
> real usage query all ran for real, then again from npm install against the public registry.
>
> Go is next, same three-release shape. Package: npmjs.com/package/inferhub-client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**Keep under 280 characters total, URL included.**

> inferhub-client 1.0.0 is on npm — audio, images, admin plane and the node close the TypeScript
> client's surface. probe() tells a coordinator from a solo node in one call. No video module —
> a method that can only throw doesn't get published. Verified live + from the registry.
>
> npmjs.com/package/inferhub-client

(check length with `wc -m` before posting — trim the last clause first if over.)

## Notes for the blog post

Slug: `inferhub-client-typescript-1-0` — created, EN visible / BG hidden, same as every prior
client post. URL: blog.devart.solutions/blog/inferhub-client-typescript-1-0

Angle: **the third phase, and it stayed boring.** Nothing here required a new design argument —
every decision (envelope-sniffing already existed since v0.1.0, read-once content is just handing
back the live `Response`, admin needs no second class) was already settled by python 18 or by
this client's own earlier phases. The one new piece of shared plumbing, `readSseFrames`, is 40
lines built directly on the NDJSON line-splitter that already existed — one byte-decoding path for
two line disciplines, not two.

Three things worth keeping if this gets expanded later:

- **`atob`/no new dependency for base64-decoded speech chunks.** Same zero-runtime-dependency
  argument every earlier phase made — the platform already has what's needed.
- **`ImageEditRequest`/`ImageVariationRequest` stay two types, not one with an `operation` field**
  — ported straight from dotnet's own reasoning: the hub's refusals become unrepresentable in the
  type system instead of merely disallowed at runtime.
- **The verification gap is the same one python 18 and js 20 already named** — this machine's node
  has no TTS/image backend and a stopped vector provider, so those success shapes stay derived
  from recorded/derived payloads rather than a live round trip. Said in the notes, not hidden.
