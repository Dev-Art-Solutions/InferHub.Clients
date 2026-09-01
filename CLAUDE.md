# CLAUDE.md

Guidance for Claude Code when working in this repository. Keep it focused on what is non-obvious —
`README.md` has the user-facing pitch and the parity table.

## What this is

Client libraries for **InferHub**, a self-hosted, Ollama-compatible inference mesh. One repository,
one hub surface, a client per language. The hub lives in its own repository
(`Dev-Art-Solutions/InferHub`) and is the authority on everything these clients call; nothing here
changes the wire, and a question about *why* an endpoint behaves as it does is answered there.

## Layout

```
dotnet/         the C# client — src/, tests/, samples/, its own solution and Directory.Build.props
python/         planned (phase 16)
js/             planned (phase 19)
go/             planned (phase 22)
spec/           the hub's client-facing surface, and response bodies recorded from a real hub
conformance/    one language-agnostic case file every client is driven against (phase 15)
plans/          build briefs. Gitignored except plans/CLAUDE.md, which is the format.
.claude/        release notes and social drafts, per package
.github/workflows/  one build + one release workflow per language, path-filtered
```

`LICENSE` and `icon.png` are repository-level: one licence and one logo, shared by every package.

## Build / test / run

```powershell
dotnet test dotnet/InferHub.Client.sln                 # 197 per TFM: 194 pass, 3 skip (env-gated integration)
dotnet format dotnet/InferHub.Client.sln --verify-no-changes
dotnet run --project dotnet/samples/BasicChat          # needs a coordinator on :5080
```

The env-gated integration suite runs only when `INFERHUB_TEST_BASEADDRESS` is set (and hits a real
chat when `INFERHUB_TEST_MODEL` is given too). **Skipped is not passed** — the skip count is part of
every release note's claim.

## Versions and tags

**Every language versions independently.** C# is at `1.x` with a public semver contract on NuGet;
the others start at `0.1.0` and earn their own `1.0.0`. A change that touches two packages is two
releases.

**Tags are `<lang>/vX.Y.Z`** — `dotnet/v1.0.1`, `python/v0.1.0`. A Go module in a subdirectory
resolves **only** from a tag prefixed with that subdirectory, so the scheme Go requires is the one
every language uses rather than Go being special-cased in the one place a mistake is silent for a
week. The bare `v0.1.0`–`v1.0.0` tags are the C# client's history and stay where they are.

## Design rules to preserve

1. **Client-facing surface only.** These packages model the HTTP surface a *caller* reaches.
   Everything a node speaks to a coordinator — SignalR, `InferenceJob`, heartbeats, tool frames — is
   out of scope, in every language, permanently. Writing a node in C# is a different project.
2. **The dependency budget is per ecosystem and it does not drift.** C#:
   `Microsoft.Extensions.Http` and `…DependencyInjection.Abstractions`, nothing else, and
   `<IsAotCompatible>` stays true with zero trim warnings. Python: `httpx` only. TypeScript: zero
   runtime dependencies. Go: stdlib only. A published package's dependencies are resolved and
   audited by every consumer, which makes this stricter than the hub's own rule 5, not looser.
   SSE, multipart and retries are hand-rolled — the C# client already does all three.
3. **Additive only, once a package is 1.0.** New capability is a new method, a new options object or
   a **new overload**. Never a changed signature, never a renamed property, never a removed one.
   Somebody is compiling against what is already published.
4. **No client holds conversation content.** Full history is re-sent each turn and **nothing is
   cached to disk by a client library**. A convenience that remembered a conversation would be a
   data-retention decision taken by a package on its consumer's behalf. This is the client-side half
   of the hub's rule 7.
5. **Count, never content.** What a client logs is a status, a duration and a model id — never a
   prompt, never a transcript, never the filename the caller chose.
6. **A node is a base address, not a second client type.** A solo node serves the same paths with
   the same bodies, so pointing the same client at a node's address *is* the node client. The
   differences are made explicit rather than left as a 404 the caller decodes: `/api/version` and
   `/api/collections` exist only on a node, the whole admin plane only on a hub, and a solo embed
   against a vendor-typed node is a `503` naming the capability.
7. **Read-once content is a stream the caller owns.** Image and video content endpoints unlink on
   read: the byte you did not keep is gone. Clients hand over the live response stream and say so;
   they never buffer somebody's 40 MB video into a `byte[]` to be friendly.
8. **`X-InferHub-Served-By` is surfaced, never interpreted.** A client reports which node or provider
   answered. It does not route, retry elsewhere, or prefer — deciding to re-send a prompt to a second
   address is a second disclosure of the same prompt.
9. **One interface per published surface, and a published interface never grows; one envelope, one
   exception.** A client models each surface in its own interface rather than by growing a published
   one — adding a member to a shipped interface breaks every implementer, which is rule 3 by another
   route. So `IInferHubOpenAiClient` sits alongside `IInferHubClient`, and `IInferHubAudioClient`
   alongside both **even though audio is served in the `/v1` dialect**: rule 3 outranks the tidiness
   of one interface per dialect, and it decides this every time. The dialects also **fail**
   differently:
   `/api/*` answers `{"error":"…"}`, `/v1/*` answers `{"error":{"message":…,"type":…}}`, and which
   envelope arrived decides the exception type — never which method was called. The exception is
   `424`: retrieval-unavailable is one condition in both dialects and keeps one exception type.
   Per-call *headers* stay shared (`InferHubCallOptions`), because the hub reads the same ones on
   both surfaces and a caller should not switch dialects to keep a prompt off a vendor's servers.
10. **A route the hub refuses by design is taught, not published as a method that throws.** The hub
    answers `501 not_supported` on `GET /v1/videos` and `POST /v1/videos/{id}/remix`, with the reason
    in the sentence — an id is itself the capability to fetch the bytes, and nothing durable holds
    the prompt that made a clip. So `IInferHubVideoClient` has neither method: rule 3 means anything
    published has to be kept, and a member that can only throw reads as "not implemented yet", which
    is the opposite of what the refusal says. What ships instead is the code
    (`VideoErrorCodes.NotSupported`), the alternative, and the recorded body in the test file.

## Testing discipline

**No test in this repository calls a live hub.** Payloads are recorded from a real one by driving it
and pasting what came back — a real `424`, a real provider error envelope, a real
`capability_unavailable`. They live as string literals in the test files until phase 15 extracts
them (see `spec/payloads/README.md` for why moving them earlier would produce a second copy nothing
checks). A test that needs a running mesh is a test CI cannot run, which makes it a test everybody
learns to skip.

**A payload that could not be recorded is marked as derived, in the file, with the reason.** Phase 9
could reach a hub but not a TTS or STT node, so its refusals are recorded and its success shapes —
`speech.audio.delta`, `speech.audio.done`, `verbose_json`, SubRip — are taken from the hub
serializers that write those bytes. Derived is weaker than recorded and the marking is what keeps
the difference visible; the release notes say the same thing out loud.

The risk that accepts, stated rather than mitigated away: a hub-side wire change is invisible to
these suites until somebody runs the clients' verification day or a user reports it.

**A behaviour discovered in one language becomes a conformance case before it becomes a fix**, or
the other three ship the same bug. The C# client parses `X-InferHub-Sources` as JSON with a
comma-separated fallback because a real hub sent both — there is no way for a Python author to learn
that except from the corpus.

## Where the rest of the context is

| Working in | Also read | Holds |
|---|---|---|
| `plans/`, writing any plan | `plans/CLAUDE.md` | the brief format, the release checklist, the budget |
| `spec/`, `conformance/` | `spec/README.md` | the hub surface map, what a node also serves, the recorded payloads |

## Release cadence

Each phase is one mini-release of **one package**: implement, keep the test slice green, bump that
package's version, tag `<lang>/vX.Y.Z`, notes in `.claude/`, then the rest of the checklist in
`plans/CLAUDE.md` — install from the public registry and run an example against a real hub, the
READMEs, the site, the blog post, FB and X, every phase without exception.

`plans/00-overview.md` indexes every phase; a brief is `plans/phase-NN-*.md` and is written **the day
its phase starts**. **When asked to start a phase, read its brief first.** When asked to write one,
read `plans/CLAUDE.md`. Only that file is in the repository; the briefs are local.

## Code style

- C#: records for DTOs, file-scoped namespaces, XML docs on everything public (`CS1591` is an error
  in Release), source-generated JSON via `InferHubJsonContext`.
- Comments are rare and explain *why*, not *what*. Match the existing tone.
- Each language is idiomatic in its own runtime. The wire is identical; the naming, the async model
  and the error convention are the ecosystem's, not C#'s translated.
