# Social copy — python/v1.0.0 (Iliya posts these manually)

## Facebook

> inferhub-client 1.0.0 is on PyPI — Python's third release closes the surface the C# client took
> eight releases to reach, done in three because the shared conformance corpus already knew
> several of the answers. From here the package is under semver: additive only.
>
> What landed: audio (transcription, and streamed speech that hands back the same live response
> whether it's buffered or framed as SSE), images (the synchronous OpenAI routes plus the whole
> async job seam — submit, watch, fetch once), the full admin plane (fleet ops, profiles, model
> lifecycle, usage, a live event stream), and probe() — one call that tells you whether the
> address on the other end is a coordinator or a solo node.
>
> No video module, on purpose: the hub permanently refuses video listing and remix with a 501, and
> a method that could only throw isn't one this client publishes.
>
> The honest part: the conformance corpus's founding case is a real bug the C# client shipped — a
> node's rerank field is a string, not a boolean, and typing it wrong threw an exception against a
> real node. Python's node types were written after that case already existed, so they were never
> wrong. Verified read-only admin/node methods against a live coordinator, both from the working
> copy and from the published package in a clean virtual environment — a real usage query came
> back matching curl row for row. What wasn't verified: a successful synthesis or image render —
> the demo node has no TTS or image backend, and the notes say so rather than imply otherwise.
>
> Package: pypi.org/project/inferhub-client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

**Keep under 280 characters total, URL included — the v0.2.0 draft ran long and had to be
shortened after the fact; count this one before posting.**

> inferhub-client 1.0.0: Python reaches audio, images, admin & probe() — the whole hub surface, in
> 3 releases instead of C#'s 8. No video module (the hub permanently refuses it). Verified live;
> what wasn't is named, not hidden.
>
> pypi.org/project/inferhub-client

(~257 characters plus the URL — checked with `wc -m`, under the 280 limit with a little room.)

## Notes for the blog post

Slug: `inferhub-client-python-1-0` — already created, EN visible / BG not written. Lands at
`blog.devart.solutions/blog/inferhub-client-python-1-0`.

Angle: **the cut point the track named three phases in advance actually held** — 15 (the corpus)
made Python's three releases cheap, and 18 is the second proof after the corpus itself that the
mechanism works, not just a feature dump. The rerank-as-string case is the sharpest illustration:
a bug the C# client shipped became a case in the shared corpus, and Python's types were correct
from their first commit because the case existed before the code did.

Three things worth keeping if this gets expanded later:

- **Buffered and streamed speech are one method, not two** — the caller's code does not change
  based on which one they asked for; only how soon the first bytes arrive does.
- **Admin lives on the same client object, not a second class** — a Python-specific decision
  (dotnet's separate `IInferHubAdminClient` exists for C#'s published-interface stability, which a
  duck-typed class does not need).
- **The verification gap is named, not hidden.** This coordinator's one node has no TTS/image
  backend; every audio/image success shape here is derived from the C# client's own recorded
  payloads and the conformance corpus, and the release notes say so plainly.
