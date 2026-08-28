# plans/ — agent context

**Scope: `plans/`.** The shape every build brief has, and where the parts of a phase end up once it
ships. **This repository is `InferHub.Clients`** — one repository, one hub surface, N language
clients — so everything here is written for a phase that ships *one package in one language*.

> **Read the root `CLAUDE.md` first**, and read the hub's `plan/CLAUDE.md` if you have the InferHub
> working copy: this file is deliberately its sibling, and where they differ it is because a package
> registry is not a container registry.

**The briefs themselves are not in the repository** — `/plans/*` is gitignored and this file is the
one exception, because it is the format rather than a brief. A fresh clone gets the format and no
plans, and a `plans/phase-NN` cited by number from another context file is a pointer into the
maintainer's working copy. Same trade the hub makes: the decisions those briefs argued end up in the
`CLAUDE.md` files, which is where a reader was going to look anyway.

## One phase → one file, written the day it starts

`plans/phase-NN-short-slug.md`, indexed in `plans/00-overview.md`. A multi-phase track gets a **thin
index** — `plans/roadmap-<slug>.md` — carrying the order, the per-phase claim, the cut point and the
invariants, and **not** the phases themselves.

**A brief is written on the day its phase starts, not up front with the track.** This repository has
already paid for the alternative once: `plans/inferhub-client-plan.md` wrote seven phases in advance
against *coordinator v2.x*, and by the time phase 6 shipped the hub had grown audio, images, video,
four cloud providers and a node that serves its own API — none of which the plan could name, because
none of it existed when the plan was written. That file is kept as the record of phases 0–6 (it is
what actually happened) and nothing new is added to it.

## One phase ships one package, and the version is that package's own

**Every language in this repository versions independently.** `dotnet/` is at `1.x` and has a public
semver contract on NuGet; `python/`, `js/` and `go/` each start at `0.1.0` and earn their own `1.0.0`.
A phase that touches two packages is two phases.

**Tags are `<lang>/vX.Y.Z`** — `dotnet/v1.1.0`, `python/v0.1.0`, `go/v0.2.0`. Not a house preference:
a Go module in a subdirectory **only** resolves from a tag prefixed with that subdirectory, so the
scheme every other package tolerates is the one Go requires. The bare `vX.Y.Z` tags from before the
monorepo are the C# client's history and stay exactly where they are.

## The shape

**Header block** — `# Phase NN — <the claim, in a sentence> (<lang>/vX.Y.Z)`, then a line carrying
`Status: TODO`, **`Format: lean`** (the marker the budget check keys on), the package and version,
**Size** (S/M/L + days), what it depends on, and **`Test slice:`** — the suites a reader must run,
named per language (`dotnet test dotnet/tests/…`, `pytest python/tests`, `npm -w js test`,
`go test ./go/...`). Then repo link, the file's own path, its track, and a `>` callout naming the
decisions to read first **by number** ("14 D2", never "the node phase") — including the hub's own,
prefixed (`hub 67 D4`), because half of what a client must get right is a decision the hub made.

**§1 Goal** — what is true today and why it is not enough, in the repo's own words and with the file
paths. Then the shape of the change, with a real request and a real response body. Then **Non-goals**,
each written as *a decision with its reason*, never a bare list.

**§2 Design decisions** — `### D1 — <a full sentence that states the claim>`, so a reader skimming
only headings gets the design. Each carries the reasoning, the **alternative that was considered and
rejected**, and which rule (1–6) it brushes. Mark the load-bearing one out loud. **Keep the body
short**: the durable argument goes to the language's `CLAUDE.md` when the phase lands, and what
belongs here is what an implementer needs *before* the code exists.

**§3 Tasks** — `- [ ]` in dependency order, each naming a **real path**. Order them so a failure is
attributable. Always include the conformance cases the phase adds, at least one **runnable example**,
the language `README.md`, the root parity table, and the `plans/00-overview.md` row.

**§4 Done when** — checkboxes, and they must include: *a caller written against the previous version
compiles and behaves identically*; **the dependency budget unchanged** (rule 2); the **test slice
green**; and **the conformance corpus green for this language**. Anything that cannot be established
without a live hub says so out loud.

**§5 Release** — the checklist below, verbatim, every phase.

**There is no §6.** What a phase turned out to be goes in `.claude/release-notes-<lang>-vX.Y.Z.md`,
which is where somebody looks for it and which is written anyway.

## §5 — the eight items, every phase, no exceptions

Fixed and ticked rather than prose to interpret, because this is the section a tired author skims.

- [ ] Bump the version **in that package's own manifest** — `dotnet/Directory.Build.props`,
      `python/pyproject.toml`, `js/package.json`; Go has no manifest and the tag *is* the version.
- [ ] `.claude/release-notes-<lang>-vX.Y.Z.md` — including **what was not established, said out loud**.
- [ ] Tag `<lang>/vX.Y.Z` → GitHub release.
- [ ] **Install the published package from its registry and run one example against a real hub.**
      Not the working copy — `dotnet add package`, `pip install`, `npm i`, `go get`, from the public
      index, in a clean directory. A green suite says nothing about what shipped: the hub learned
      this on its own verification day when three features were unreachable on the published images.
      A phase that publishes nothing says so in the notes instead of going quiet.
- [ ] Flip `Status:` in the brief, the track index and `plans/00-overview.md`.
- [ ] The language's `README.md`, **and** the root README's parity table.
- [ ] `inferhub.devart.solutions` — the client section for that language, and "What's next".
- [ ] Blog post → FB → X.

READMEs before the site because the site quotes them; the post last because it links the release.
**Batching the docs and the posts to the end of a track is how `.claude/social-v*.md` accumulated
unposted copy for six releases** — copy written a week late describes what you remember rather than
what shipped. Three facts about the blog, each learned by hitting it:

- The connector is **insert-only with a locking slug** — `list_posts` first, then create the post
  **visible in one shot**. There is no update and no delete, so a draft you meant to fix is a post
  you cannot fix.
- **No shell commands in the post HTML.** The Cloudflare WAF in front of the blog blocks the
  *request*, not the command. Show the JSON, not the `curl`.
- Posts live at `blog.devart.solutions/blog/<slug>`. `devart.solutions/blog` 404s and that is not a
  failed post.

## Budget

**A lean brief is 250 lines.** A track index is a brief for budget purposes and gets the same 250.
The number is what a phase of this size actually needs once the re-argued rules are gone. **A budget
fitted to the present never binds**, so this one is deliberately below where a comfortable author
would land — if a phase will not fit, it is two phases.

## House voice

State the failure the decision prevents, concretely ("a Python caller gets `None` and cannot tell a
model with no answer from a node that dropped"). Prefer a rejected alternative to an adjective.
**Never write a caveat that a later phase makes false without deleting it everywhere.**

## Related context

- The rules a plan may not quietly amend: the root `CLAUDE.md`
- What the clients are implementing, and what is recorded rather than guessed: `spec/CLAUDE.md`
- The per-language decisions: `dotnet/CLAUDE.md`, `python/CLAUDE.md`, `js/CLAUDE.md`, `go/CLAUDE.md`

## Decisions recorded here

### The format itself

**D1 — A brief is written the day its phase starts.** Above, with `inferhub-client-plan.md` and the
hub surface it could not name. **Considered and rejected: the shape that file used**, every phase
written up front in one document — it reads as thoroughness and it is mostly prediction.

**D2 — A decision is written into the scoped `CLAUDE.md` as the phase lands, and the brief keeps the
claim.** Otherwise every decision is argued twice and the two copies drift the moment a later phase
amends one. **Considered and rejected: dropping the rejected alternatives from the brief too** —
that is the one part that decays into "why on earth is it like this" once the alternative is dead.

**D3 — `plans/CLAUDE.md` is committed and the briefs are not.** A format file that vanished in a
fresh clone would leave the root `CLAUDE.md` pointing at nothing, which teaches a reader that the
context does not exist rather than that the index is wrong. **Considered and rejected: committing all
of `plans/`** — publishing the internal briefs is its own decision, not a side effect of a docs
phase. The `/plans/*` plus `!` form is required because git does not descend into an excluded
*directory*.

**D4 — `inferhub-client-plan.md` is left exactly as it is.** It is the record of what phases 0–6
decided, it is cited by number, and a rewritten record says what we would decide today — which is
not what a record is for.
