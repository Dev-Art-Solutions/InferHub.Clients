# InferHub.Clients

Client libraries for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) — a self-hosted,
Ollama-compatible inference mesh. One repository, one hub surface, a client per language.

| Language | Package | Version | Status |
|---|---|---|---|
| **C#** — [`dotnet/`](dotnet/) | [`InferHub.Client`](https://www.nuget.org/packages/InferHub.Client/) on NuGet | `1.7.1` | shipping |
| **Python** — [`python/`](python/) | [`inferhub-client`](https://pypi.org/project/inferhub-client/) on PyPI | `0.1.0` | shipping (core) |
| **TypeScript** — [`js/`](js/) | npm | — | planned |
| **Go** — [`go/`](go/) | `pkg.go.dev` | — | planned |

Every client talks to the same HTTP surface, and **a node is a base address, not a different
client**: a solo InferHub node serves the same paths with the same bodies as a coordinator, so
pointing a client at `http://your-gpu-box:11435` is the whole of "run it locally".

## What each client covers

Empty columns are the honest state, not an oversight — each one names the phase that fills it.

| Capability | C# | Python | TypeScript | Go |
|---|---|---|---|---|
| Chat & generate, blocking | ✓ | ✓ | — | — |
| Chat & generate, streaming | ✓ | ✓ | — | — |
| Embeddings (batch + legacy) | ✓ | ✓ | — | — |
| Vector data plane (upsert/query/retrieve) | ✓ | — | — | — |
| RAG retrieval, with source ids | ✓ | — | — | — |
| Admin: fleet ops, collections, live SSE | ✓ | — | — | — |
| Transient retries, trim/AOT clean | ✓ | n/a | n/a | n/a |
| OpenAI dialect (`/v1/*`) + provider steer | ✓ | — | — | — |
| Audio — transcription and streamed speech | ✓ | — | — | — |
| Images — sync, async jobs, read-once content | ✓ | — | — | — |
| Video — the OpenAI dialect, read-once content | ✓ | — | — | — |
| Ingestion, documents, chunks, search & rerank | ✓ | — | — | — |
| Admin: profiles, model lifecycle, usage | ✓ | — | — | — |
| A node as a first-class target | ✓ | — | — | — |
| Core client | ✓ | ✓ | phase 19 | phase 22 |
| Retrieval | ✓ | phase 17 | phase 20 | phase 23 |
| Modalities, admin, node, and 1.0 | ✓ (7–14 done) | phase 18 | phase 21 | phase 24 |

## Layout

```
dotnet/       the C# client, its tests and its runnable samples
python/       the Python client (core, v0.1.0), its tests and its runnable examples
js/           planned
go/           planned
spec/         the hub's client-facing surface, and the payloads recorded from a real hub
conformance/  one language-agnostic case file every client is driven against (13 cases so far)
```

`LICENSE` and `icon.png` are repository-level and shared by every package.

## Quick start (C#)

```
dotnet add package InferHub.Client
```

```csharp
services.AddInferHubClient(o =>
{
    o.BaseAddress = new Uri("http://localhost:5080");  // or a solo node's own address
    o.ApiKey = "<your-client-api-key>";
});
```

Full documentation: [`dotnet/README.md`](dotnet/README.md).

## Releases

Tags are `<lang>/vX.Y.Z` — `dotnet/v1.7.1`, and `python/v0.1.0` when it exists. A Go module in a
subdirectory only resolves from a tag prefixed with that subdirectory, so the scheme Go requires is
the one every language uses. Each package versions independently; the bare `v0.1.0`–`v1.0.0` tags
are the C# client's history from before this repository held more than one language.

## Links

- InferHub server: <https://github.com/Dev-Art-Solutions/InferHub>
- Product page: <https://inferhub.devart.solutions>
- Blog: <https://blog.devart.solutions>

## License

MIT — see [LICENSE](LICENSE).
