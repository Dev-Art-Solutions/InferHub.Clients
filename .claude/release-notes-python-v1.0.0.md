# python/v1.0.0 — modalities, admin, the node, and 1.0

The third and last Python phase in the 15–18 cut point: audio, images, the admin plane and the node
join core (`v0.1.0`) and retrieval (`v0.2.0`). Python now covers the whole surface this client's
design reaches, and `1.0.0` starts this package's semver contract — additive-only from here (root
`CLAUDE.md` rule 3).

## New

- **Audio** — `transcribe`/`transcribe_document` (`POST /v1/audio/transcriptions`, multipart, the
  file field last) and `create_speech`/`stream_speech` (`POST /v1/audio/speech`). Buffered and
  streamed synthesis are the same method with the same return shape (`SpeechAudio` wrapping a live
  `httpx.Response` the caller closes) — not one line of caller code differs, mirroring dotnet
  phase-9 D2. `stream_speech` yields one `SpeechChunk` per `speech.audio.delta`/`.done` frame; the
  terminal frame carries `usage` (a true zero count for a phoneme model) and `characters` read once
  from `X-InferHub-Speech-Characters`. A `speech.audio.error` frame raises
  `InferHubOpenAiException`.
- **Images** — `generate_image`/`edit_image`/`create_image_variation` (the synchronous `/v1/images/*`
  routes) and the async job seam (`submit_image_generation`/`_edit`/`_variation`, `list_image_jobs`,
  `get_image_job`, `watch_image_job`, `open_image_content`, `cancel_image_job` over
  `/api/images/jobs`). Content is read-once: `open_image_content` hands back a live stream, and a
  retry after reading is a `410 job_expired`. `ImageEditRequest`/`ImageVariationRequest` are two
  types, not one with an `operation` field, so the hub's "a variation takes no prompt/mask" refusals
  are unrepresentable in this client's types rather than merely disallowed.
- **No video module.** The hub `501`-refuses `GET /v1/videos` and remix, permanently — root rule 10
  means a method that could only throw does not get published. `VideoErrorCodes.NOT_SUPPORTED`
  names the code; the fix is documented (send a new request) rather than a method that throws.
- **Admin** — fleet ops, admin vector collections, `stream_admin_events` (SSE), node profiles, the
  full model lifecycle (pull/delete/warm, tool-model variants, the fleet matrix, `ensure_model`),
  usage queries and the clients listing — all on the same `InferHubClient`/`AsyncInferHubClient`, no
  second class. A caller reaches them with an admin key instead of a client key; see
  `_admin.py`'s module docstring for why Python does not mirror dotnet's separate
  `IInferHubAdminClient` (that split exists for C#'s published-interface stability, which a
  duck-typed Python class does not need).
- **The node** — `probe()` reads `/api/status` once and returns `InferHubTargetProbe(kind="hub" |
  "solo_node", ...)`, discriminated on whether the body carries `mode`. Five node-only methods
  (`get_node_version`, `list_node_collections`, `get_node_collection`, `create_node_collection`,
  `drop_node_collection`) are named with a `node` in their name so a `404` against the wrong target
  reads as a name, not a surprise — and are distinct methods from the admin-plane collection
  methods (different auth, different route, different shape; a node has no fleet to place a
  replica on). `NodeRetrievalInfo.rerank` is typed `str` from its first commit — the conformance
  corpus's founding case (`node-status-rerank-is-a-string`) is the regression test for a bug dotnet
  shipped in `v1.7.0` and this client never had the chance to.
- **`InferHubOpenAiException`** (new, subclass of `InferHubError`) — `raise_for_status` now sniffs
  the error envelope's *shape*, not the route, to decide which exception to raise: a
  `{"error":{"message",...,"code"}}` object (used by `/v1/*` **and** `/api/images/jobs`, which
  answers the OpenAI shape from an `/api/*` path) raises this type with `error_code`/`param`/
  `error_type` intact; a `{"error":"..."}"` string raises the existing `InferHubError`; `424` is
  always `InferHubRetrievalException`, in both dialects. One function serves every surface.
- 4 new runnable examples: `audio_speech.py`, `images_job.py`, `admin_fleet.py`, `node_probe.py`.

## Verified against this repo's own live coordinator (3.37.0, localhost:5080)

Every read-only admin/node method was run against it directly, not just the mocked suite:

- `probe()` → `kind="hub"`, 1 node, version parsed correctly.
- `list_nodes()`, `list_model_matrix()`, `query_usage()` (4 real rows, field-for-field matching
  `curl`), `list_clients()`, `list_profiles()` — all parsed real responses correctly.
- `list_admin_collections()` → `404`, matching this hub's own corpus provider being `stopped`
  (the same limitation phase 17's notes recorded — not new, confirmed unchanged).
- `get_node_version()` against this hub (not a node) → `404`, exactly the "wrong target, not wrong
  version" the method's docstring promises.
- `generate_image(...)` → `404 model 'llava' not found`, as `InferHubOpenAiException` — this hub's
  one node advertises only `chat`/`embed` (`tools.enabled=false`), so no image or audio capability
  was ever exercised against a live route.

**Not established: a successful transcription, synthesis, image render, or image-job round trip.**
This hub's only node has `tools.enabled=false` and no TTS/image backend — every audio/image success
shape here is derived from the dotnet client's own recorded and derived payloads (phase 9/10,
themselves marked derived where dotnet's own hub-on-the-day could not reach a real modality
backend) and the conformance corpus, not from a live response this phase captured itself. Also not
established: `pull_model`/`delete_model`/`warm_model`/the tool-model variants/`ensure_model` and any
admin mutation beyond a read — deliberately not run against the live fleet's real node, same
judgment call phase 13 made (a client-library release check is not licence to pull or delete a
model on someone's GPU box).

## Compatibility

Additive over `v0.2.0`: every phase-18 member is new, nothing existing changed shape. `httpx` is
still the only runtime dependency. `pytest python/tests`: 98 pass, 3 skip (named — the
`/v1/chat/completions` OpenAI chat dialect, which stays dotnet-only per roadmap D3 and was never in
Python's scope).

See `python/README.md` for the full API table and the four new examples under `python/examples/`.
