# dotnet/v1.7.0 — A node is a base address, not a second client

The last phase of the 7–14 catch-up track. A solo InferHub node already served nearly this whole
client surface identically to a coordinator — chat, generate, streaming, embeddings, the vector
data-plane, RAG headers, the OpenAI dialect, audio, images, video and ingestion. This release closes
the gap: telling the two apart, and reaching the handful of routes that exist on only one side. No
new client type — pointing `IInferHubClient` at a node's own address already was the node client for
everything else, and stays that way.

## New

- **`ProbeAsync`** — one `GET /api/status`, read once and discriminated on the presence of `mode`.
  Returns `InferHubTargetProbe { Kind, Version, HubStatus?, NodeStatus? }`. A node's document always
  carries `mode: "solo"`; the hub's carries no `mode` field at all, ever — that asymmetry is the whole
  signal, not a guess based on which base address was configured.
- **`GetNodeVersionAsync`** (`GET /api/version`) and the node-only collection lifecycle —
  `ListNodeCollectionsAsync` / `GetNodeCollectionAsync` / `CreateNodeCollectionAsync` /
  `DropNodeCollectionAsync` (`/api/collections`) — a client-key equivalent of the hub's admin-gated
  `IInferHubAdminClient.ListCollectionsAsync`, because a node has no admin plane to gate it behind.
  Calling any of these four against a hub is a plain `404`; the whole admin plane is a plain `404`
  against a node. Neither is a `403` — both are an honest absence, documented on the interface rather
  than left for a caller to decode.
- **`NodeStatusResponse`** and its nested `NodeBackendInfo`/`NodeConcurrency`/`NodeGpuInfo`/
  `NodeRetrievalInfo` model the node's own `/api/status` document — deliberately smaller than the
  hub's: no fleet array, no queue block, no replica count, because a node with no coordinator has no
  concept of any of them.

## Corrected

Root `CLAUDE.md` rule 6 said a solo embed against a vendor-typed node is "a `503` naming the
capability." Reading the hub's own `LocalApiEndpoints.BackendCannot`/`CapabilityDisabled` shows two
distinct refusals: a backend that structurally cannot serve a capability (no Anthropic embeddings
API) is a permanent `501`, no `Retry-After`; a capability an operator disabled is a temporary `503`
with `Retry-After`. `InferHubException.StatusCode`/`RetryAfter` already model both — no new exception
type, just the corrected doc comments on `EmbedAsync`/`EmbedLegacyAsync` and the rule itself fixed in
the same commit rather than left saying something the source no longer supports.

## Not established this session

**No live coordinator or solo node was reachable to verify `ProbeAsync` against a real target of
either kind**, or to confirm the recorded `501`/`503` embed-refusal bodies against a real vendor-typed
node. Said out loud rather than silently deferred, matching phase 6 and phase 9's precedent for what
a release note claims versus what it could actually run. Everything here is verified against: the
hub's own endpoint source (`LocalStatusEndpoints.cs`, `LocalCollectionEndpoints.cs`,
`LocalApiEndpoints.cs`), and 13 new unit tests over recorded/reconstructed bodies matching that
source's exact shapes and message text.

## Compatibility

Fully additive. A caller written against `dotnet/v1.6.0` compiles and behaves identically — every new
member is a new method on the existing `IInferHubClient`, nothing moved or renamed. Dependency budget
unchanged (`Microsoft.Extensions.Http` + `…DependencyInjection.Abstractions`), `<IsAotCompatible>`
stays `true`, zero trim/AOT warnings on Release build. `dotnet test dotnet/InferHub.Client.sln`: 259
total (256 pass, 3 env-gated skips), both TFMs — 13 of the 256 are new this phase.

See `dotnet/README.md`'s [A node as a
target](https://github.com/Dev-Art-Solutions/InferHub.Clients/blob/main/dotnet/README.md#a-node-as-a-target)
and the new `samples/NodeTarget` for a runnable walk-through against either kind of target.
