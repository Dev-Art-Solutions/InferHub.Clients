# dotnet/v1.7.1 — patch: `NodeStatusResponse.Retrieval.Rerank` is a string, not a bool

A same-day fix to `1.7.0`, found by doing exactly what `plans/CLAUDE.md` §5's install-and-verify step
exists to catch: `1.7.0` was installed from nuget.org into a clean directory and driven against a
real coordinator (localhost:5080, one meshed node) and a real solo node — `ghcr.io/dev-art-solutions/inferhub-node:latest`
run in Docker with `LocalApi:Enabled=true`, `Coordinator:Enabled=false`,
`LocalApi:Retrieval:Enabled=true`, against a live Ollama backend.

`ProbeAsync` against the hub worked correctly (`Kind.Hub`, node count, a clean `404` from
`GetNodeVersionAsync`). Against the solo node with retrieval enabled, it threw:

```
System.Text.Json.JsonException: The JSON value could not be converted to System.Nullable`1[System.Boolean].
Path: $.retrieval.rerank
```

The real node sends `"retrieval":{"rerank":"none", ...}` — a string, the config-level rerank *mode*
(`"none"` or `"llm"`, from `LocalRetrievalOptions.Retrieval.Rerank`), not a boolean. `1.7.0`'s
`NodeStatusResponse.Retrieval.Rerank` was typed `bool?`, guessed rather than verified against a real
response, because no node was reachable when that phase was written. This is unrelated to
`RetrievalOptions.Rerank` (the per-request `X-InferHub-Rerank` flag on chat/generate) — same name,
different field, different shape, and the two must not be confused.

## Fixed

- `NodeStatusResponse.Retrieval.Rerank` retyped `bool?` → `string?`.
- The test that recorded this shape (`InferHubNodeTargetTests`) now uses the actual body captured
  from the real node, not a guessed one.

## Verified against real targets (this time clean)

Reinstalled `1.7.1` from nuget.org into a clean directory. Both `ProbeAsync` calls, `GetNodeVersionAsync`,
the full `/api/collections` lifecycle (`ListNodeCollectionsAsync` → `CreateNodeCollectionAsync` →
`GetNodeCollectionAsync` → `DropNodeCollectionAsync`, including the `404`→`null`/`false` paths), and
an `EmbedAsync` call against the node's real Ollama backend all completed without error. `GetNodeVersionAsync`
against the hub correctly surfaced a `404`.

## Compatibility

Fully additive relative to `1.6.0`; relative to `1.7.0` this is the narrowest possible fix — one
property's type, on a type that shipped hours earlier and had no reasonable chance of being depended
on for its exact (wrong) shape yet. `dotnet test dotnet/InferHub.Client.sln`: 259 total (256 pass, 3
env-gated skips), both TFMs.

**If you installed `1.7.0`**, upgrade — reading `NodeStatusResponse.Retrieval` against any solo node
with retrieval enabled throws.
