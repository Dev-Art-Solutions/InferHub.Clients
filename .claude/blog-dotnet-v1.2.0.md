# Blog post — dotnet/v1.2.0, as published

**Posted 2026-08-31**, live at
<https://blog.devart.solutions/blog/inferhub-client-audio-streamed-speech> (id
`6a94aee8ebb94d33b93d42cc`), EN visible / BG hidden, created in one shot after `list_posts`
confirmed the slug was free. Verified `200` with the title and the error envelope in the body.

**The connector answered `Missing sessionId` on six attempts across the first ~10 minutes of the
release and recovered on its own after ~25**, which is the window this failure mode has always had —
unlike "Unable to verify organization membership", which does not recover. The post was parked here
complete rather than created blind, because the connector is insert-only with a locking slug and
`list_posts` was down too, so there was no way to confirm the slug was free.

No shell command appears in the HTML: the Cloudflare WAF in front of the blog blocks the *request*,
not the command, so the install line is prose rather than a `<pre>`. The copy below is the record of
what was published, not a draft.

---

- **slug**: `inferhub-client-audio-streamed-speech`
- **title_en**: `A synthesis you hear before it is finished`
- **excerpt_en**: `InferHub.Client 1.2.0 adds audio in both directions — and the streamed
  synthesis cost the caller nothing, because the library had already decided never to hand back a
  byte[]. Plus the two things about the wire that no schema would have told us.`
- **isVisible_en**: `true` · **isVisible_bg**: `false` · **author**: omitted, so the connector's
  own default stands — no earlier release's collateral records this field being set, and it cannot
  be corrected afterwards.

## content_en

```html
<p>The C# client for <a href="https://github.com/Dev-Art-Solutions/InferHub">InferHub</a> can speak
and listen as of 1.2.0. That is a changelog line. The part worth a post is what it cost, which was
almost nothing, and why.</p>

<h2>The same method, twice as useful</h2>

<p>Synthesis on our hub has two shapes. Ask for the whole file and you get the whole file. Ask for
it with <code>stream_format: "audio"</code> and the hub writes the bytes as it makes them, so the
first sentence is playable while the fourth is still being synthesised.</p>

<p>Here is the client's entire answer to that distinction:</p>

<pre><code class="language-csharp">await using var speech = await audio.CreateSpeechAsync(
    SpeechRequest.Create("piper", "Hello from the fleet."));

await using var file = File.Create("speech.wav");
await speech.Audio.CopyToAsync(file);</code></pre>

<p>Set <code>StreamFormat</code> and <strong>not one byte of that changes</strong>. There is no
second method, no callback, no <code>Stream destination</code> parameter.</p>

<p>That is not cleverness, it is a bill coming due in our favour. When we added images, we wrote
down a rule: <em>long content is a stream the caller owns, never a <code>byte[]</code> allocated to
be friendly.</em> The argument at the time was about images and video — a 40 MB clip buffered into
an array is a large-object-heap allocation and a second copy of somebody's content. Audio was not
in scope; the rule was written for a different modality entirely.</p>

<p>But a client that never buffered has nothing to change when the server stops buffering. The
streaming support was already there, a year early, in a decision made about pictures.</p>

<h2>A zero that is a measurement</h2>

<p>The framed form of the same call yields one chunk per <code>speech.audio.delta</code> and then a
terminal frame carrying a usage block and no audio:</p>

<pre><code class="language-json">{"type":"speech.audio.done","usage":{"input_tokens":0,"output_tokens":0,"total_tokens":0}}</code></pre>

<p>Three zeros. Every instinct says <em>placeholder</em> — the field the server had to emit because
the schema demanded it, and filled with nothing.</p>

<p>They are a true count. Piper is a phoneme model; it tokenized nothing, so nothing is the honest
number. A client that treats zero as "missing" and falls back to "usage not available" reports the
wrong thing about a real measurement. The number that reconciles with a bill is elsewhere entirely
— a <em>character</em> count, on a response header, because characters are what a synthesis is
metered in.</p>

<p>So the client yields that frame like any other, and the caller checks <code>Usage</code>:</p>

<pre><code class="language-csharp">await foreach (var chunk in audio.StreamSpeechAsync(request))
{
    if (chunk.Usage is { } usage)
    {
        Console.WriteLine(chunk.Characters);   // what was metered
        continue;
    }

    await player.WriteAsync(chunk.Audio);
}</code></pre>

<p>Which is exactly what the OpenAI dialect already asked of a caller: on a streamed chat, the frame
with an <em>empty</em> <code>choices</code> array is the usage frame. Learn the rule once.</p>

<h2>Two things the wire does that a schema would not have told us</h2>

<p><strong>Every form field must be written before the file part.</strong> Above a size threshold
our hub routes a transcription from the leading form fields <em>while the audio is still
arriving</em> — that is the whole point of streaming an upload rather than buffering it — so a
field that shows up after the file is refused. Below that threshold the ordinary multipart reader
takes any order at all.</p>

<p>That second half is what makes it dangerous. A client that writes the file first is correct on
every recording anybody tests with, and wrong on the first real one: a forty-minute meeting, in
production, months after the code was written, with a <code>language</code> the hub never saw.</p>

<p><strong>And <code>503</code> is not <code>404</code>.</strong> "The fleet holds this model but no
node is currently doing this kind of work" comes back as a <code>503</code> with
<code>capability_unavailable</code> and a <code>Retry-After</code>. "No node holds this model at all"
is a <code>404</code>. One is worth retrying in thirty seconds and the other never is, so the client
surfaces the code rather than flattening both into "the request failed".</p>

<pre><code class="language-json">{"error":{"message":"no node currently provides 'speak' for model 'gemma:2b'","type":"api_error","param":null,"code":"capability_unavailable"}}</code></pre>

<p>Neither of those is derivable from a schema, an OpenAPI document or a type. Both were learned by
driving a real hub and reading what came back.</p>

<h2>Transcription, and the file that stays a file</h2>

<pre><code class="language-csharp">await using var input = File.OpenRead("meeting.wav");

var transcript = await audio.TranscribeAsync(
    TranscriptionRequest.FromStream("whisper-1", input, "meeting.wav", "audio/wav"));</code></pre>

<p>You get text, language, duration and timed segments. Ask instead for <code>srt</code> or
<code>vtt</code> and a different method hands back <strong>the hub's own bytes, untouched</strong> —
because a subtitle file is a file, and parsing it into a transcript object would throw away the cue
timings that were the reason to ask for it. (The separator before the milliseconds is a comma in
SubRip and a period in WebVTT. Rendering that yourself is how you produce a file one player accepts
and another rejects.)</p>

<p>What the client does <em>not</em> do: transcode. There is no audio library in it, no encoder, no
resampler. Ask for a format the fleet's worker cannot produce and you get a <code>400</code> naming
what it can — never a quiet substitution, which is how you end up holding a corrupted file with a
confident content type and finding out in a media player three days later.</p>

<h2>What this release does not establish</h2>

<p>The hub we verified against runs one node serving chat and embeddings, with the tool runtime
switched off. There was no text-to-speech or speech-to-text node to reach.</p>

<p>So: every <em>refusal</em> in the test suite is recorded from that live hub — eleven of them,
pasted back with their escapes intact. The <em>success</em> shapes are derived from the hub's own
serializers, and each one is marked in the test file as derived, with the reason. The published
package was installed from nuget.org into a clean directory and driven against that hub, where both
audio routes reached routing and were refused there — which does establish that a real hub parses
the multipart body this client builds, and that the failure path works end to end.</p>

<p>It does not establish that any audio came out. No transcript was produced and nothing was
synthesised. We are not claiming a latency number, a sample rate, or that it sounds good.</p>

<p>Additive throughout: nothing from 1.1 changed. 125 tests per target framework, two dependencies,
still AOT-clean.</p>

<p>The package is <code>InferHub.Client</code> on nuget.org, version 1.2.0, and the code is at
<a href="https://github.com/Dev-Art-Solutions/InferHub.Clients">github.com/Dev-Art-Solutions/InferHub.Clients</a>.
Next in the line: images on the async job seam.</p>
```
