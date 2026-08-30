# Social copy — dotnet/v1.2.0 (Iliya posts these manually)

Verify the facts at posting time, not from memory: that 1.2.0 is on nuget.org, that the parity
table's Audio row reads ✓ for C#, and that `dotnet/samples/Speech` runs against the hub you point it
at. **One fact this release cannot claim** — see the last section — so do not let the copy drift
into "we tested the audio".

## Facebook

> InferHub.Client 1.2.0 is out, and the C# client can finally speak and listen.
>
> `IInferHubAudioClient` covers /v1/audio/transcriptions and /v1/audio/speech — same hub, same
> address, same key as the other two surfaces. Transcription comes back parsed, with language,
> duration and timed segments; ask for srt or vtt instead and you get the hub's own bytes
> untouched, because a subtitle file is a file and reinterpreting it loses the cue timings that
> were the reason to ask.
>
> The half we like: synthesis you hear before it is finished. Stream it as frames and each one is
> playable audio; the terminal frame carries the count and no audio. And the buffered call and the
> streamed one are literally the same method — the client hands you the live response stream either
> way, so the code that writes a file does not change when the hub starts writing it as it is made.
> That falls out of a rule this library already had: somebody's audio is never buffered into a
> byte[] to be friendly.
>
> One detail worth knowing if you ever build the multipart body yourself: every form field must be
> written before the file part. Above a size threshold the hub routes the request from the leading
> fields while your bytes are still arriving, so a field after the file is a 400 — and below that
> threshold any order works, which is exactly why getting it wrong is correct on every test file
> and wrong on the first real one.
>
> Additive: nothing from 1.1 changed. 125 tests per target framework, two dependencies, still
> AOT-clean, and no audio library anywhere in it.
>
> Package: nuget.org/packages/InferHub.Client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

> InferHub.Client 1.2.0 — audio, both directions.
>
> Transcription with segments, or the hub's own srt/vtt verbatim. Speech you can play before it is
> finished — and the buffered call and the streamed one are the same method, because the client
> never buffered your audio in the first place.
>
> Additive. Two dependencies. nuget.org/packages/InferHub.Client

## Notes for the blog post

Slug: `inferhub-client-audio-streamed-speech`. `list_posts` first, then create it **visible in one
shot** — the connector is insert-only with a locking slug, so a draft you meant to fix is a post you
cannot fix. **No shell commands in the HTML**: the Cloudflare WAF blocks the *request*, not the
command, so show the C# and the JSON envelope rather than a `curl` or a `dotnet add package` line
inside a `<pre>`. The post lands at
`blog.devart.solutions/blog/inferhub-client-audio-streamed-speech`.

Angle: **the streamed synthesis costing the caller nothing**. "We added audio" is a changelog line.
The interesting claim is that `CreateSpeechAsync` is one method for both the whole file and the
chunked one, because the library had already decided never to hand back a `byte[]` — a design rule
from the images work paying for itself in a modality it was not written for. Three good details:

- The terminal frame's **three zeros are a true count**, not a placeholder — a phoneme model
  tokenized nothing — and the number that reconciles with a bill is a character count on a header.
  A client that treats zero as "missing" reports the wrong thing.
- **Fields before the file**, and why the tolerant path is the dangerous one.
- **503 + `capability_unavailable` is not 404**: "the fleet has this model but nobody is doing this
  kind of work right now" carries a Retry-After; "nobody holds the model" does not.

**The honest line, and it belongs in the post rather than in a footnote:** the hub used for
verification had no TTS or STT node, so every refusal in the suite is recorded from a real hub and
the success shapes are derived from the hub's own serializers. No audio was synthesised and no
transcript was produced. Nothing in the post should claim otherwise — no latency numbers, no sample
rates, no "sounds great".
