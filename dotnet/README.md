# InferHub.Client

[![NuGet](https://img.shields.io/nuget/v/InferHub.Client.svg)](https://www.nuget.org/packages/InferHub.Client/)
[![NuGet downloads](https://img.shields.io/nuget/dt/InferHub.Client.svg)](https://www.nuget.org/packages/InferHub.Client/)
[![build and test](https://github.com/Dev-Art-Solutions/InferHub.Client/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/Dev-Art-Solutions/InferHub.Client/actions/workflows/build-and-test.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A small, typed .NET client for [InferHub](https://github.com/Dev-Art-Solutions/InferHub) —
a self-hosted, Ollama-compatible inference mesh.

Point it at a coordinator, pass a Bearer token, and call chat, generate, model listing
and status from C# with typed requests, dependency injection, and no heavy dependencies.

> **v1.0.0** — the full mesh surface from one small package: blocking + streaming inference,
> embeddings (batch + legacy), the vector data plane (upsert / query / retrieve / get / delete),
> opt-in RAG retrieval (grounded chat/generate with source ids), and the admin client (fleet
> ops, collection lifecycle, live SSE event stream). Trim- and AOT-friendly via source-generated
> serialization, with optional off-by-default transient retries. The public API is now stable
> under [semantic versioning](#versioning).

## Install

```
dotnet add package InferHub.Client
```

Targets `net9.0` and `net10.0`.

## Quick start

```csharp
using InferHub.Client;
using InferHub.Client.Configuration;
using InferHub.Client.Models.Ollama;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddInferHubClient(o =>
{
    o.BaseAddress = new Uri("http://localhost:5080");
    o.ApiKey = "<your-client-api-key>";
});

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IInferHubClient>();

var chat = await client.ChatAsync(new ChatRequest
{
    Model = "llama3",
    Messages = new[]
    {
        new ChatMessage { Role = "user", Content = "Say hi in one word." }
    },
    Stream = false
}, CancellationToken.None);

Console.WriteLine(chat.Message?.Content);
```

## API surface

`IInferHubClient` (client key):

| Method | Endpoint |
|---|---|
| `ListModelsAsync` | `GET /api/tags` |
| `GenerateAsync` (blocking) | `POST /api/generate` with `stream:false` |
| `ChatAsync` (blocking) | `POST /api/chat` with `stream:false` |
| `ChatStreamAsync` | `POST /api/chat` with `stream:true` (NDJSON → `IAsyncEnumerable<ChatResponse>`) |
| `GenerateStreamAsync` | `POST /api/generate` with `stream:true` (NDJSON → `IAsyncEnumerable<GenerateResponse>`) |
| `EmbedAsync` | `POST /api/embed` (batch — single string or string[]) |
| `EmbedLegacyAsync` | `POST /api/embeddings` (legacy single `prompt`) |
| `UpsertAsync` | `POST /api/vector/{collection}/upsert` |
| `QueryAsync` | `POST /api/vector/{collection}/query` |
| `RetrieveAsync` | `POST /api/vector/{collection}/retrieve` |
| `GetRecordAsync` | `GET /api/vector/{collection}/{id}` (→ `null` on 404) |
| `DeleteRecordAsync` | `DELETE /api/vector/{collection}/{id}` (→ `false` on 404) |
| `GetStatusAsync` | `GET /api/status` |
| `PingAsync` | `GET /health` |

Chat/generate (blocking and streaming) also take an optional `InferHubCallOptions` for
per-call RAG retrieval and sticky conversation routing — see [RAG retrieval](#rag-retrieval).

`IInferHubAdminClient` (admin key):

| Method | Endpoint |
|---|---|
| `ListNodesAsync` | `GET /api/admin/nodes` |
| `CordonAsync` / `UncordonAsync` | `POST /api/admin/nodes/{nodeId}/cordon` / `…/uncordon` |
| `DeregisterAsync` | `POST /api/admin/nodes/{nodeId}/deregister` |
| `DrainAsync` (extension) | client-side cordon + poll until `inFlight == 0` |
| `ListCollectionsAsync` | `GET /api/admin/vector/collections` |
| `GetCollectionAsync` | `GET /api/admin/vector/collections/{collection}` (→ `null` on 404) |
| `CreateCollectionAsync` | `POST /api/admin/vector/collections` |
| `DropCollectionAsync` | `DELETE /api/admin/vector/collections/{collection}` |
| `RebuildAsync` | `POST /api/admin/vector/collections/{collection}/rebuild` |
| `StreamAdminEventsAsync` | `GET /api/admin/stream` (SSE → `IAsyncEnumerable<AdminEvent>`) |

### Streaming

```csharp
await foreach (var chunk in client.ChatStreamAsync(new ChatRequest
{
    Model = "llama3",
    Messages = new[] { new ChatMessage { Role = "user", Content = "Stream me a haiku." } }
}, cancellationToken))
{
    Console.Write(chunk.Message?.Content);
}
```

The enumerator stops as soon as a chunk arrives with `done:true`. A terminal error
chunk (`{ "error": …, "done": true }`) is surfaced as `InferHubException` — the client
never retries mid-stream, so a partial answer plus a clean exception is the contract.
Cancelling the token throws promptly out of the `await foreach`.

Request models carry an extension bag (`AdditionalProperties`), so any unknown fields
from the Ollama contract pass through untouched — you can hand-set `options`, `format`,
tool definitions, etc. without waiting on the client to type them.

### Embeddings

```csharp
// Single input.
var single = await client.EmbedAsync(
    EmbedRequest.FromText("nomic-embed-text", "hello, world"));

// Batch — one vector per input, same order.
var batch = await client.EmbedAsync(EmbedRequest.FromTexts(
    "nomic-embed-text",
    new[] { "InferHub", "self-hosted", "inference mesh" }));

Console.WriteLine(batch.Embeddings.Length);   // 3
Console.WriteLine(batch.Embeddings[0].Length); // model dimension
```

`EmbedAsync` targets the modern batch endpoint (`/api/embed`); `EmbedLegacyAsync` wraps
`/api/embeddings` for drop-in Ollama callers. An empty vector list on a 200 response is
treated as malformed and surfaced as `InferHubException` — the client never returns a
silent zero-vector result.

### Vectors

Text in, ranked matches out. The coordinator embeds `text` on a node for you, so you never
have to hold a model client-side. Needs the coordinator running with `VectorStore:Enabled=true`
and the collection already created (see [Fleet + vector admin](#fleet--vector-admin)).

```csharp
using InferHub.Client.Models.Vector;

// Upsert — embed text on a node, keep the original as an opaque payload.
await client.UpsertAsync("docs", VectorUpsert
    .FromText("doc-1", "InferHub is a self-hosted inference mesh.", "nomic-embed-text")
    .WithPayload(new { title = "About" })
    .WithMetadata(new Dictionary<string, string> { ["kind"] = "doc" }));

// Query — text in, closest matches out.
var matches = await client.QueryAsync("docs",
    VectorQuery.FromText("what is InferHub?", "nomic-embed-text", k: 3));

foreach (var m in matches)
    Console.WriteLine($"{m.Score:F3}  {m.Id}");

// Read the payload back into your own type.
var record = await client.GetRecordAsync("docs", "doc-1"); // null if absent
var title = record?.Payload.As<Doc>()?.Title;

await client.DeleteRecordAsync("docs", "doc-1"); // false if it wasn't there
```

Pass a raw vector instead of text with `VectorUpsert.FromVector` / `VectorQuery.FromVector`.
`payload` is exposed as a `JsonElement?`; call `.As<T>()` to deserialize it. `GetRecordAsync`
returns `null` and `DeleteRecordAsync` returns `false` on a 404; every other non-success
status is an `InferHubException`. `RetrieveAsync` is the same call as `QueryAsync` under the
RAG-oriented name. See `samples/MiniRag` for a runnable embed-then-query loop.

### RAG retrieval

Ground a chat or generate call in a vector collection with one option object — the
coordinator retrieves, augments the prompt in-flight, and echoes the grounding record ids:

```csharp
using InferHub.Client.Rag;

var grounded = await client.ChatAsync(request,
    InferHubCallOptions.ForRetrieval("docs", k: 4));

Console.WriteLine(grounded.Message?.Content);
Console.WriteLine(string.Join(", ", grounded.SourceIds ?? []));  // retrieved record ids
```

`InferHubCallOptions` also carries `ConversationId` for sticky routing
(`ForConversation("...")`). When retrieval is unavailable and the coordinator is configured
with `OnMissing=error`, the call throws `InferHubRetrievalException` (a `424`). Calls
without options behave exactly as before. See `samples/GroundedChat`.

### Fleet + vector admin

Everything under `/api/admin/*` lives on `IInferHubAdminClient`, registered by the same
`AddInferHubClient` call but authenticated with `AdminApiKey` — a client key alone never
surfaces admin methods.

```csharp
var admin = provider.GetRequiredService<IInferHubAdminClient>();

// Fleet: cordon a node, wait for in-flight work to finish, bring it back.
var drained = await admin.DrainAsync("node-1");        // cordon + poll (client-side)
await admin.UncordonAsync("node-1");

// Vector collections: lifecycle + replica health.
await admin.CreateCollectionAsync("docs", dimension: 768, distance: "cosine");
var detail = await admin.GetCollectionAsync("docs");   // placement, underReplicated, stats
await admin.RebuildAsync("docs");                      // force a heal-to-target re-push

// Live ops feed: fleet snapshots + vector.* lifecycle events over SSE.
await foreach (var ev in admin.StreamAdminEventsAsync(new AdminStreamOptions()))
{
    Console.WriteLine(ev.IsSnapshot
        ? $"snapshot: {ev.Nodes!.Count} node(s)"
        : $"#{ev.Sequence} {ev.Event} {ev.Collection}");
}
```

The `AdminStreamOptions` overload reconnects with exponential backoff when the stream
drops (auth failures are never retried); the plain overload is a single connection. See
`samples/FleetOps` for a runnable fleet walk-through.

## Auth

- Non-loopback calls need `ApiKey` (attached as `Authorization: Bearer <key>` by a
  `DelegatingHandler`).
- Loopback calls to the coordinator skip auth by default (unless the coordinator sets
  `Auth:RequireAuthForLoopback=true`).
- `/health` is always open.
- Admin routes require a **separate** `AdminApiKey`, sent only by `IInferHubAdminClient`;
  a client key alone never surfaces admin methods.

## Errors

Any non-success HTTP response is surfaced as `InferHubException`, carrying:

- `StatusCode` — the HTTP status
- `Message` — the coordinator's `{ "error": "…" }` body if present

The client treats `404` (model or collection missing) as a signal worth checking with
`StatusCode`, and `424 Failed Dependency` (retrieval unavailable) gets its own subtype,
`InferHubRetrievalException`.

## Resilience

Transient retries are **off by default**. Turn them on for brief coordinator restarts or
network blips:

```csharp
services.AddInferHubClient(o =>
{
    o.BaseAddress = new Uri("http://localhost:5080");
    o.MaxRetryAttempts = 3;                       // 0 = off (default)
    o.RetryBaseDelay = TimeSpan.FromMilliseconds(200); // doubles each retry…
    o.MaxRetryDelay = TimeSpan.FromSeconds(5);         // …capped here
});
```

Retries apply **only to idempotent requests** — `GET`/`HEAD` (model list, status, health,
record fetch, admin reads, and the initial SSE connect) — that fail with a connection error
or a `5xx`/`408` status. A chat, generate, embed, upsert or delete is **never** silently
re-run, and a stream is **never** retried mid-flight: a partial answer plus a clean exception
stays the contract. The per-call timeout is `Options.Timeout` (100s by default).

## Trimming & AOT

The typed request/response surface is serialized through a source-generated
`JsonSerializerContext`, so the library is trim- and Native-AOT-friendly
(`<IsAotCompatible>true</IsAotCompatible>`) with no reflection over the DTO graph.

The two generic payload escape hatches deserialize the *caller's* own type, which reflection
can't preserve under trimming/AOT — so they come in two overloads:

```csharp
// Reflection-based — fine for JIT; flagged by the trim/AOT analyzers.
upsert.WithPayload(new Doc { Title = "About" });
var doc = record.Payload.As<Doc>();

// AOT-safe — pass a source-generated JsonTypeInfo<T> from your own context.
upsert.WithPayload(doc, MyJsonContext.Default.Doc);
var doc2 = record.Payload.As(MyJsonContext.Default.Doc);
```

## Versioning

From 1.0.0 the client follows [Semantic Versioning](https://semver.org):

- **Patch** (`1.0.x`) — fixes, no API change.
- **Minor** (`1.x.0`) — additive, source-compatible: new methods, new overloads, new options.
- **Major** (`2.0.0`) — reserved for a breaking change to the public API.

New capabilities land as overloads (as the per-call RAG options did), so existing call sites
keep compiling across the whole `1.x` line. Client versions stay independent of the
coordinator's; `1.0.0` targets the coordinator's `v2.x` HTTP surface.

## Links

- InferHub server: <https://github.com/Dev-Art-Solutions/InferHub>
- Product page: <https://inferhub.devart.solutions>
- Blog: <https://blog.devart.solutions>

## License

MIT — see [LICENSE](LICENSE).
