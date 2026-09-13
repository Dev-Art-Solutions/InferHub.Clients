# go/v1.0.0 — modalities, admin, the node, and 1.0

The third and last of the planned Go phases: `github.com/Dev-Art-Solutions/InferHub.Clients/go`
closes its surface with audio, images, the admin plane and the node — mirroring python 18 and
js 21, which mirror dotnet's own eight-phase catch-up. **This is a semver promise**:
additive-only from here.

## New

- **Audio** (`media.go`): `Transcribe`/`TranscribeDocument` (`POST /v1/audio/transcriptions`,
  multipart, always requesting `verbose_json` for `Transcribe` regardless of what the caller asked
  for — dotnet D6), `CreateSpeech`/`StreamSpeech` (`POST /v1/audio/speech`, the live
  `*http.Response` handed back either way — dotnet D2). `StreamSpeech` forces SSE framing; a
  `speech.audio.error` frame surfaces as `*Error` with `Kind == KindOpenAI` instead of ending the
  stream silently.
- **Images** (`media.go`): `GenerateImage`/`EditImage`/`CreateImageVariation` (sync
  `/v1/images/*`) and `SubmitImageGeneration`/`SubmitImageEdit`/`SubmitImageVariation` plus
  `ListImageJobs`/`GetImageJob`/`WatchImageJob`/`OpenImageContent`/`CancelImageJob` (the async
  `/api/images/jobs` seam — `WatchImageJob` is an SSE iterator that stops at a terminal job
  state). `ImageOptions` becomes the `X-InferHub-Image-*` extension headers, never body fields.
- **The admin plane** (`admin.go`): fleet ops, admin-gated vector collections (distinct from the
  node-only ones — different auth/route/shape), `StreamAdminEvents` (SSE), node profiles
  (`PutProfile`'s `Name`/`Revision` ignored on write — dotnet D6), model lifecycle commands,
  `QueryUsage` (a `UsageQuery` struct, not four positional parameters — Go has no keyword args)
  and `ListClients` (never carries a key — dotnet D5).
- **The node** (`admin.go`): `Probe` — one `GET /api/status`, discriminated on whether the body
  carries `mode` — returns a typed `TargetProbe{Kind: TargetHub | TargetSoloNode, ...}`.
  `NodeStatusResponse.Retrieval.Rerank` is typed `string` from the start (the conformance corpus's
  founding case: dotnet `v1.7.0` typed it `bool?` and threw against a real node with retrieval on).
  Node-only `GetNodeVersion` and `/api/collections` lifecycle round it out.
- **No video module** — the hub permanently `501`-refuses video listing/remix (root rule 10), so
  no throw-only method is published; `inferhub.VideoErrorCodeNotSupported` names the refusal
  instead, same call C#/Python/TypeScript already made.
- `go/examples/nodeprobe/main.go` — probe a base address, print hub vs. solo-node.

## Conformance

`probe` and `openai-images-submit` kinds are now covered — `node-status-rerank-is-a-string`,
`hub-status-has-no-mode-field`, `503-capability-unavailable-carries-retry-after`. Split moves from
`v0.2.0`'s 7/13 to **10/13** — the same split js/v1.0.0 reached over the identical corpus. The
remaining 3 (`openai-chat`/`openai-chat-stream` kinds — the `/v1/chat/completions` dialect) stay
dotnet-only, per python 18/js 21's own "legitimately skipped" carve-out.

## Verified against the published module, from the public proxy

`go get github.com/Dev-Art-Solutions/InferHub.Clients/go@v1.0.0` into a clean scratch module
outside the repo, resolved from `proxy.golang.org` off the pushed `go/v1.0.0` tag. A small program
calling `Probe`, `ListNodes` and a media method against that installed module compiled and ran.

**What was not established**, same gap as `go/v0.2.0`: no InferHub coordinator was reachable from
this environment at implementation or tag time, so the calls above confirm compilation and correct
request construction, not a live round trip — every call returned a transport error, not a hub
response. The `httptest`-based unit and conformance suites (10/13 corpus cases plus the new
`media_test.go`/`admin_test.go` files) are the actual correctness evidence, by design (no test in
this repository calls a live hub). A live pass against every published Go release is phase 25's
job — the track's dedicated verification day, which exists in the roadmap for exactly this reason.

## Compatibility

Fully additive over `v0.2.0`: no existing method's signature changed. `go.mod`'s `require` block
stays empty — `mime/multipart`, `encoding/base64` and `net/url` join the existing stdlib set.
`go test ./...`: unit tests plus the conformance runner at 10 pass / 3 named-skip / 13 accounted
for. `go vet ./...` and `gofmt -l .` clean. Go 1.22+.

**This closes the Go track's per-language work** (phases 22–24) — the same three-phase shape
Python and TypeScript already proved. Next is phase 25: every published package, installed from
its own public registry, driven against a real coordinator *and* a real solo node.

See `go/README.md` for the full API table (every method through `v1.0.0`) and `go/examples/` for
runnable programs, including the new `nodeprobe`.
