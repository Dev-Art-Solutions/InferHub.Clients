# dotnet/v1.6.0 — Admin catch-up: profiles, model lifecycle, usage, clients

`IInferHubAdminClient` catches up with five sub-surfaces the hub's `/api/admin/*` group grew since
this client froze against coordinator v2.x: node profiles, model lifecycle, the fleet-wide model
matrix, usage accounting and the configured-clients view. Eleven new methods on the existing admin
interface — nothing new to register in DI, no breaking change to anything shipped in 1.0–1.5.

## New

- **Node profiles** — `ListProfilesAsync`, `GetProfileAsync` (→ `null` on 404), `PutProfileAsync`,
  `DeleteProfileAsync`, `GetNodeProfileAsync`. A profile is desired state, never a command: the hub
  can narrow a node's capabilities, tools or concurrency cap, and can never widen them.
  `PutProfileAsync`'s `name`/`revision` are ignored on write — the hub assigns both from the route
  and its own counter regardless of what is sent (verified against the coordinator source).
  `GetNodeProfileAsync` returns desired beside effective, plus every refusal the node reported and
  why — the field an operator actually needs when "I turned it on and nothing happened".
- **Model lifecycle** — `PullModelAsync`, `DeleteModelAsync`, `WarmModelAsync`, and the tool-scoped
  `PullToolModelAsync`/`DeleteToolModelAsync` for a tool's own catalogue (e.g. a diffusion recipe).
  Each returns the hub's literal `202` body (`commandId`, `reused`); progress rides the existing
  `model-progress` SSE frame on `StreamAdminEventsAsync` — no second way to poll a pull invented.
- **The fleet model matrix** — `ListModelMatrixAsync` (`GET /api/admin/models`): every model, which
  nodes hold it, and which nodes can manage models at all.
- **`EnsureModelAsync`** — `POST /api/admin/models/{model}/ensure`, returning the hub's full
  placement reasoning (`effectiveTarget`, `nonManageableHolders`, `eligibleCandidates`,
  `cordonedNodesSkipped`, `shortfall`, a `note`) rather than collapsing it to a boolean.
- **Usage** — `QueryUsageAsync` (`GET /api/admin/usage`, with `from`/`to`/`clientId`/`model`
  filters). Aggregates only — the ledger holds counts, never a prompt or a completion, and could not
  leak one even if asked. `UsageRow` models the wire as it exists today: the hub's route projects
  only token/request counts, not the richer audio/character/image/video unit totals its internal
  `UsageAggregate` type carries — recorded so a later phase that finds the route grown a
  `unitBreakdown` field treats it as new, not a bug here.
- **Clients** — `ListClientsAsync` (`GET /api/admin/clients`): ids, configured limits, and live
  window consumption. Never a key — `ClientConfig.Key` never leaves the hub process, so there is no
  field on `ClientRow` to notice is always empty.

## Verified against a real, running coordinator (3.37.0)

The published `1.6.0` package was installed from nuget.org into a clean directory and driven
against a real InferHub coordinator with a real connected node — not the test suite's recorded
payloads. `ListProfilesAsync`, `ListModelMatrixAsync`, `QueryUsageAsync` (real historical usage
rows came back), `ListClientsAsync`, `GetNodeProfileAsync` (a real node, no profile assigned:
`status=none`, effective capabilities `[chat, embed]`), and a full `PutProfileAsync` →
`GetProfileAsync` → `DeleteProfileAsync` round trip (with an inert selector naming no real node) all
matched their modelled shapes exactly. The empty-selector `400` came back with the coordinator's own
sentence, byte for byte.

**Not run against the live fleet, deliberately:** `PullModelAsync`, `DeleteModelAsync`,
`WarmModelAsync`, the tool-model variants, and `EnsureModelAsync` — every one of them would have
pulled, deleted or warmed a model on somebody's real GPU node, which is not this check's call to
make. Those five stay verified against the coordinator's own endpoint source and the test suite's
recorded shapes only. Said here rather than left implicit.

## Compatibility

Fully additive. A caller written against `dotnet/v1.5.0`'s `IInferHubAdminClient` compiles and
behaves identically. Dependency budget unchanged (`Microsoft.Extensions.Http` +
`…DependencyInjection.Abstractions`), `<IsAotCompatible>` stays `true`, zero trim warnings.
`dotnet test dotnet/InferHub.Client.sln`: 246 total (243 pass, 3 env-gated skips), both TFMs.

See `dotnet/README.md`'s [Admin: profiles, model lifecycle, usage and
clients](https://github.com/Dev-Art-Solutions/InferHub.Clients/blob/main/dotnet/README.md#admin-profiles-model-lifecycle-usage-and-clients)
and the extended `samples/FleetOps` for a runnable walk-through of all of it.
