# Social copy — dotnet/v1.3.0 (Iliya posts these manually)

Verify the facts at posting time, not from memory: that 1.3.0 is on nuget.org, that the parity
table's Images row reads ✓ for C#, and that `dotnet/samples/ImageJob` runs against the hub you point
it at. **One fact this release cannot claim** — see the last section — so do not let the copy drift
into "we rendered a picture with it".

## Facebook

> InferHub.Client 1.3.0 is out, and the C# client can ask for pictures.
>
> `IInferHubImagesClient` covers the three synchronous routes — generations, edits, variations — and
> the async job seam under /api/images/jobs: submit, list, watch, fetch, cancel. Same hub, same
> address, same key as the other three surfaces.
>
> The two ways of asking are the same request; what differs is whether you wait. Submit it as a job
> and you get a place in line, then one SSE frame per change carrying the whole job document —
> state, step, total steps — so "step 7 of 28 on node-1" is something you read rather than
> something you poll for.
>
> The detail we spent the most care on is a retry we had to switch off. Fetching an image from a
> finished job unlinks the bytes at the hub: read it once and it is gone, on purpose, because the
> hub is not an image archive. That fetch is a GET, which is everything a transient-retry handler
> needs to re-send it after a dropped connection — and the retry would collect a 410 where the
> picture used to be. So the request is marked never-retry at the handler rather than written up as
> a caveat in a README that nobody reads twice. A caveat would have been true, unreadable, and one
> release from being forgotten.
>
> Two smaller ones. A variation takes no prompt and no mask, so the variation request type simply
> does not have those fields — two of the hub's 400s that you cannot write rather than two you find
> out about over the network. And the picture you fetch tells you what it *is*: flat or
> equirectangular, declared by the worker, never guessed from the aspect ratio, because a 2:1
> photograph and a 2:1 panorama are the same bytes in the same shape and only one of them opens
> correctly in a headset.
>
> Additive: nothing from 1.2 changed. 163 tests per target framework, two dependencies, still
> AOT-clean — and no image library anywhere in it, because nothing here decodes a pixel.
>
> Package: nuget.org/packages/InferHub.Client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

> InferHub.Client 1.3.0 — images, and the job seam under them.
>
> Submit, watch it step through over SSE, fetch the bytes. That fetch is read-once at the hub, so
> the client refuses to retry it: the retry would collect a 410 where your picture used to be.
>
> Additive. Two dependencies. No image library. nuget.org/packages/InferHub.Client

## Notes for the blog post

Slug: `inferhub-client-images-and-the-job-seam`. `list_posts` first, then create it **visible in one
shot** — the connector is insert-only with a locking slug, so a draft you meant to fix is a post you
cannot fix. **No shell commands in the HTML**: the Cloudflare WAF blocks the *request*, not the
command, so show the C# and the JSON rather than a `curl` or a `dotnet add package` line inside a
`<pre>`. The post lands at `blog.devart.solutions/blog/inferhub-client-images-and-the-job-seam`.

Angle: **the retry we had to turn off**. "We added images" is a changelog line. The interesting
claim is that read-once content makes a *generic* resilience feature actively destructive, and that
the fix is a marker on the request rather than a sentence in the docs. Four good details:

- **The retry that eats a picture**, above. Two tests hold it: the content fetch reaches the
  transport exactly once with retries set to 3, and an ordinary GET with the same options still
  retries — so the first test is about the marker rather than about retries being off.
- **`MediaJob`, not `ImageJob`.** The hub renders video jobs through the same serializer, so a type
  named for one modality would have to be renamed the day video lands — and a published type is not
  renamed. Naming for the seam rather than for today's caller, one release early.
- **Two refusals you cannot write.** A variation has no prompt and no mask because the hub says so
  in two 400s; separate request types make those unrepresentable.
- **0,75 is a 400.** The extension knobs are headers, and every number leaves invariantly
  formatted — a strength sent from a Bulgarian or German machine as `0,75` is refused, and it is
  the class of bug that only reproduces on some developers' laptops. The test sets the thread
  culture to bg-BG and asserts the wire.

**The honest line, and it belongs in the post rather than in a footnote:** the hub used for
verification has one node serving chat and embed with no tool runtime, so there was no image node
to reach. Every refusal in the suite is recorded from that real hub — fifteen of them — and the
success shapes are derived from the hub's own serializers and marked as derived. **No image was
generated.** Nothing in the post should claim otherwise: no render times, no sample pictures, no
"the seam repair works". What *was* established end to end is the request shape and the whole
failure path, from the published package against the real hub — including the real hub reading an
`X-InferHub-Image-Steps` header this client wrote, and parsing the multipart form it builds.
