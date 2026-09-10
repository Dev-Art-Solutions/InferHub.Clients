# js/v1.0.0 — modalities, admin, the node, and 1.0

The third TypeScript release, closing the gap with dotnet's phases 9–14 and python's `v1.0.0`:
audio, images, the admin plane and the node. This starts TypeScript's `1.0.0` semver contract —
additive-only from here.

## New

- **Audio** — `transcribe`/`transcribeDocument` (`POST /v1/audio/transcriptions`, multipart, always
  requesting `verbose_json` for the parsed path regardless of what the caller asks for) and
  `createSpeech`/`streamSpeech` (`POST /v1/audio/speech`). `createSpeech` hands back the live
  `Response` whether or not the caller asked for streaming — not one line of caller code differs.
  `streamSpeech` yields one `SpeechChunk` per SSE frame; a `speech.audio.error` frame throws
  `InferHubOpenAiException`, and the terminal `speech.audio.done` frame is yielded like any other
  (a usage of three zeros is a true count for a phoneme model that tokenized nothing).
- **Images** — `generateImage`/`editImage`/`createImageVariation` (the synchronous `/v1/images/*`
  routes) and the async job seam (`submitImageGeneration`/`submitImageEdit`/`submitImageVariation`,
  `listImageJobs`, `getImageJob`, `watchImageJob`, `openImageContent`, `cancelImageJob`) over
  `/api/images/jobs`. `ImageEditRequest`/`ImageVariationRequest` are two separate types rather than
  one with an `operation` field, so the hub's refusals ("a variation takes no prompt") are
  unrepresentable in this client's types instead of merely disallowed. `openImageContent` is
  read-once — the caller consumes `response.body`/`.arrayBuffer()`, never buffered by this client.
- **Admin plane** — fleet ops (`listNodes`/`cordon`/`uncordon`/`deregister`), admin vector
  collections (`listAdminCollections`/`getAdminCollection`/`createAdminCollection`/
  `dropAdminCollection`/`rebuildAdminCollection`), `streamAdminEvents` (SSE), node profiles
  (`listProfiles`/`getProfile`/`putProfile`/`deleteProfile`/`getNodeProfile`), model lifecycle
  (`pullModel`/`deleteModel`/`warmModel`/`pullToolModel`/`deleteToolModel`/`listModelMatrix`/
  `ensureModel`), usage and clients (`queryUsage`/`listClients`). No second class: admin methods
  live on the same `InferHubClient` as everything else — a caller reaches them by constructing with
  an admin key, since a plain TS class has no interface-segregation concern the way dotnet's DI
  registration does.
- **The node** — `probe()` (`GET /api/status`, discriminated on whether the body carries `mode`),
  and the node-only vector collection lifecycle (`getNodeVersion`, `listNodeCollections`,
  `getNodeCollection`, `createNodeCollection`, `dropNodeCollection`) — never the same method as the
  admin-gated `listAdminCollections` (different auth, different route, different shape).
  `NodeRetrievalInfo.rerank` is typed `string` from the first commit, not `boolean` — the
  conformance corpus's founding case, a regression test for a bug this client never shipped.
- **No video module.** The hub permanently `501`-refuses `GET /v1/videos` and remix, so per root
  rule 10 no throw-only method was published — `VideoErrorCodes.NOT_SUPPORTED` plus README/test
  coverage instead.
- New shared plumbing: `readSseFrames` in `_stream.ts`, built on the existing `readNdjsonLines`
  line-splitter — one byte-decoding path serves both NDJSON and SSE framing.

## Verified against a real, running InferHub coordinator

This repo's own machine runs a live `3.37.0+1366b40` coordinator. From the working copy:
`probe()` → `kind: "hub"`, 1 node, version matched. `listNodes` (1 node), `listModelMatrix`
(29 models × 1 node), `queryUsage` (4 real rows), `listClients` (0 configured) all ran for real —
this coordinator's `RequireAuthForLoopback` is `false` and `AdminApiKeys` is empty, so no admin key
was needed against `localhost`. `generateImage`, `createSpeech` and `listNodeCollections` all threw
the expected `404`s (`model 'sdxl' not found`, `model 'piper' not found`, no node-collection route)
— this coordinator's only node has no TTS/image backend and its vector/corpus provider is
`"status": "stopped"`, the same known gap python 18 and js 20 already recorded on this machine.

**Not established:** a successful transcription/synthesis/image render, a live node-collection
round trip, or `streamAdminEvents`/`watchImageJob` against real SSE traffic — derived from
dotnet/python's own recorded/derived payloads and the conformance corpus instead, same as python
`v1.0.0`'s own release notes.

## Compatibility

Additive over `v0.2.0`: every new method and type is new, nothing existing changed shape.
`js/package.json`'s `dependencies` is still `{}` — `fetch`, `FormData`, `ReadableStream` and `atob`
are all the platform, no new runtime dependency. `npm -w js test`: 83 pass, 3 skip (named — the
OpenAI chat dialect, dotnet-only per `roadmap-polyglot-clients` D3). `npm -w js run typecheck`
clean.

See `js/README.md` for the full API table, the Audio/Images/Admin/Node/Video sections, and
`js/examples/{audio-speech,images-job,node-probe,admin-fleet}.ts` for runnable scripts.
