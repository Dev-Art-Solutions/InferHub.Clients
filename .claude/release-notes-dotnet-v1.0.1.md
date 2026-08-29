InferHub.Client 1.0.1 — the repository this package comes from is now **InferHub.Clients**, and
it holds a client per language rather than one. **No code changed.** Not a single `.cs` file
differs from 1.0.0, the public API is identical, the dependency list is identical, and the same
79 tests per target framework pass exactly as they did.

What changed is the package's own metadata, and it is visible on nuget.org:

- **`RepositoryUrl` and `PackageProjectUrl`** now point at
  `github.com/Dev-Art-Solutions/InferHub.Clients`. 1.0.0 links a repository that no longer
  exists under that name — GitHub redirects it, but the package page was quietly wrong.
- **The readme shipped inside the package** is now the C# client's own (`dotnet/README.md`)
  rather than the repository README, which as of this release is a document about four
  clients and not about this one.

## Why the repository moved

InferHub has grown seven versions of client-facing surface since this package froze at 1.0.0 —
the OpenAI dialect, audio in both directions, images, video, ingestion, cloud providers, and a
node that serves nearly the whole surface on its own address. Covering that from C# alone was
never the plan; Python, TypeScript and Go clients are, and four repositories for four readings
of one wire is how four clients quietly disagree about it.

So: one repository, a folder per language, and **C# is one of them rather than the root**.

```
dotnet/       this package — src/, tests/, samples/
python/  js/  go/     planned
spec/         the hub's client-facing surface, and which of it a node also serves
conformance/  one language-agnostic case file every client will be driven against
```

**Tags are now `<lang>/vX.Y.Z`** — this release is `dotnet/v1.0.1`. That is not a house
preference: a Go module in a subdirectory resolves **only** from a tag carrying that prefix, so
the scheme Go requires is the one every language uses rather than Go being special-cased in the
one place a mistake stays silent for a week. The bare `v0.1.0`–`v1.0.0` tags are this client's
history and stay exactly where they are.

## What this release does **not** establish, said out loud

- **The seven older release notes and five blog posts cite pre-move paths** (`src/InferHub.Client`,
  `tests/InferHub.Client.Tests`). They are the record of what happened and they are left alone;
  the READMEs and the product site are corrected forward.
- **`spec/payloads/` is empty.** The plan assumed the recorded response bodies were files that
  could be moved; they are inline C# string literals across five test files, so extracting them
  is an edit to the test project — and this release's whole acceptance test is that the tests
  pass *unchanged*. It goes to the phase that builds the conformance runner, where an extracted
  payload is checked by something rather than by whoever pasted it.
- **`conformance/`, `python/`, `js/` and `go/` are placeholders.** Each holds a README naming the
  phase that fills it and **no manifest** — an empty `pyproject.toml` or `go.mod` is a package
  that resolves and does nothing, which is worse than an absent one.
- **The test count in the 1.0.0 notes was wrong** and is corrected here: 79 per target framework
  (76 pass, 3 env-gated integration skips), on net9.0 and net10.0. 1.0.0 said "85 tests — 76
  pass, 3 skip", which does not add up. Nothing regressed; the arithmetic was.

## Install

```
dotnet add package InferHub.Client --version 1.0.1
```

Nothing in your code needs to change. If you pinned `1.0.0`, there is no reason to move except
that the package page will link the right repository.
