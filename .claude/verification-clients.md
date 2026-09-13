# Phase 25 — the verification day

`plan/roadmap-polyglot-clients.md` D8: every published client package, installed from its own
public registry, driven against a real coordinator **and** a real solo node. No external blocker —
unlike InferHub's own provider verification day (parked on vendor keys that never came), this one
needs only a hub, a node, and four registries that already exist.

**Status: DONE.** All four packages resolved from their public registries and were driven against
both targets successfully. Two real behavioral discrepancies were found on the hub/node side (noted
below); none is a client defect.

## The environment, once

| | |
|---|---|
| OS | Windows 11 Pro 10.0.26200 |
| .NET | 10.0.301 |
| Node.js | v25.8.1 / npm 11.11.0 |
| Python | 3.12.0 (via `py -3 -m venv`) |
| Go | 1.23.4 (portable install — no Go toolchain on `PATH`; see the `go-toolchain-portable-install` memory) |
| Backend | Ollama, `http://localhost:11434/`, 29 models already pulled locally |
| Date | 2026-09-13 |

**The hub**: `InferHub.Coordinator` built from source (`InferHub` repo, `dotnet build -c Release`)
and run directly (`dotnet InferHub.Coordinator.dll`), `ASPNETCORE_URLS=http://localhost:5080`,
`Auth:NodeEnrollmentSecret` set via env so a real node could enroll. `RequireAuthForLoopback` is
false by default, so no API key was needed for these calls (same finding phase 21/22 already
recorded on this machine).

**The meshed node**: `InferHub.Node` built the same way, run against that coordinator with a
matching `Coordinator:EnrollmentSecret`, `Backend:Type=ollama` at the default
`http://localhost:11434/`. Registered as `local-node`, reported 29 models, capabilities
`chat`+`embed`.

**The solo node**: a second `InferHub.Node` process, `Coordinator:Enabled=false`,
`LocalApi:Enabled=true` on `http://localhost:5081`, same Ollama backend. `GET /api/status` answers
`mode: "solo"` as the discriminator every client's `probe()`/`Probe()` reads.

Neither target used Docker or a published InferHub image — both are dev builds from source, run
directly. This differs from InferHub's own verification days (which pull `ghcr.io/...` tags), but
is the same "real coordinator, real node, real backend" shape phase 22's original single-language
verification used on this machine, just standing up both targets deliberately this time instead of
relying on whatever happened to already be running.

## Coverage: one representative call per surface, per package, per target

Every package: `probe()`/`Probe()`, blocking chat against `qwen2.5:0.5b`, and `embed`/`Embed` against
`nomic-embed-text:latest`. Go additionally got a streaming-chat and an unknown-model-404 check,
carried over unchanged from `go/v0.1.0`'s own verification pass.

| Package | Registry | Version | Coordinator | Solo node |
|---|---|---|---|---|
| `InferHub.Client` | NuGet | `1.7.1` | ✓ `Probe` (`Hub`), `ChatAsync` (`"Hi! How can I assist you today?"`, `servedBy=node`), `EmbedAsync` (768-dim) | ✓ `Probe` (`SoloNode`), `ChatAsync`, `EmbedAsync` (768-dim), `servedBy=node-solo` |
| `inferhub-client` (Python) | PyPI | `1.0.0` | ✓ `probe()` (`hub`), `chat(ChatRequest(...))`, `embed(EmbedRequest(...))` (768-dim) | ✓ `probe()` (`solo_node`), `chat`, `embed` (768-dim) |
| `inferhub-client` (npm) | npm | `1.0.0` | ✓ `probe()` (`hub`), `chat()`, `embed()` (768-dim) | ✓ `probe()` (`solo_node`), `chat()`, `embed()` (768-dim) |
| `github.com/Dev-Art-Solutions/InferHub.Clients/go` | Go module proxy | `v1.0.0` | ✓ `Probe` (`TargetHub`), `Chat`, `ChatStream` (14 chunks), unknown-model → `404`, `Embed` (see finding 2 below) | ✓ `Probe` (`TargetSoloNode`), `Chat`, `ChatStream` (10 chunks), unknown-model → `502` (finding 1), `Embed` (768-dim) |

Every install was a clean one: `dotnet add package`/`pip install` into a fresh venv/`npm install`
into a fresh `package.json`/`go get`, each into a directory outside any of the four repos, resolved
from the public registry rather than a local build or project reference.

## Findings — not client defects, recorded because this is the day that catches them

**1. A solo node answers an unknown model with `502`, a coordinator with `404`.** Same request
(`model: "definitely-not-a-real-model"`), same backend, two different statuses depending on which
target answered. A coordinator's `404` names the model directly; the solo node's `502` reads
`"model 'definitely-not-a-real-model' not found"` in the body but the *status* suggests an upstream
failure rather than a client request error. Every client here surfaces whatever the server sent
faithfully (root rule: which envelope arrived decides the exception type, never which method was
called) — this is a real inconsistency in InferHub itself, not a client bug, and is InferHub's to
fix, not InferHub.Clients'. Filed here rather than silently worked around in a client, because a
client that special-cased this away would be less trustworthy, not more.

**2. `Embed`/`embed` against a bare `nomic-embed-text` (no `:latest` tag) 404s on the coordinator
but resolves on the solo node.** The coordinator's model registry matches the tag advertised by the
node exactly (`nomic-embed-text:latest`); the solo node forwards the bare name straight to Ollama,
which defaults an untagged reference to `:latest` itself. Every check in the table above used the
explicit tag once this was found, and it reproduces with `curl` directly against `/api/embed` — not
a serialization or routing difference between the four clients, all four hit the exact same wire.
Also InferHub's to reconcile, not a client-side finding.

## What this does not establish

- **Retrieval, ingestion and search were not exercised.** Neither the coordinator nor either node
  had a vector/corpus provider configured for this pass (`Vector:` is absent from both
  `appsettings.json`s used) — the same "provider is stopped on this machine" gap phases 17, 20 and
  23 already recorded, carried forward rather than newly discovered. Every language's retrieval
  surface is covered by its own `httptest`/`nock`/`responses`-equivalent unit suite and the shared
  conformance corpus instead.
- **Audio, images, admin-plane and node-profile calls were not exercised against a live target.**
  The core surface (chat, generate, embed, status/probe) is what every language's own release
  already verified live at least once; this day's job was confirming the *published* artifact
  resolves and reaches *both* a hub and a solo node, not re-running every method every release
  already covered on its own day.
- **No cloud provider dialect** (OpenAI/Anthropic/Gemini/OpenRouter) — none of the four clients has
  one; this is unrelated to InferHub's own, separately-dropped provider verification day.
- Both InferHub processes here were **built from source**, not pulled as published container
  images the way InferHub's own verification days do. The four *client* packages were the ones
  installed from a public registry — that is this day's actual subject (D8: "every published
  *package*"). Standing up the hub/node pair itself from published `ghcr.io` images was not
  attempted and is a reasonable next refinement, not a gap in what this day claims.

## Result

**All four published InferHub.Clients packages, at their current released versions, install
cleanly from their public registries and correctly reach both a real coordinator and a real solo
node.** This closes phase 25 and, with it, the entire `roadmap-polyglot-clients.md` track
(phases 15–25).
