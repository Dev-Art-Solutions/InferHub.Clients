InferHub.Client 1.1.0 — the C# client speaks the hub's **second dialect**, and a caller can now
say **which vendor may see the prompt**. Additive throughout: no signature changed, no property was
renamed, `IInferHubClient` gained nothing, and code written against 1.0.x compiles and behaves
identically.

## The OpenAI dialect

`IInferHubOpenAiClient` — same base address, same client key, registered by the same
`AddInferHubClient(...)`:

| Method | Endpoint |
|---|---|
| `CreateChatCompletionAsync` / `StreamChatCompletionAsync` | `POST /v1/chat/completions` |
| `CreateCompletionAsync` / `StreamCompletionAsync` | `POST /v1/completions` |
| `CreateEmbeddingsAsync` | `POST /v1/embeddings` |
| `ListModelsAsync` / `GetModelAsync` | `GET /v1/models`, `GET /v1/models/{id}` |

It is a **separate interface rather than more methods on `IInferHubClient`** on purpose: adding a
member to a published interface breaks every caller holding a test double or a decorator, which is
exactly what the 1.x contract forbids. `IInferHubAdminClient` already set that shape.

Three things the wire actually does, each of which the client now gets right:

- **`data: [DONE]` is not JSON.** It ends the stream and is never deserialized. A stream that ends
  *without* it ends without an exception — the hub sends a terminal frame with
  `finish_reason: "stop"` when a node drops mid-answer, and throwing there would discard the partial
  answer the caller is already holding.
- **A frame with an empty `choices` array is the usage frame.** It is yielded, not skipped: with
  `stream_options.include_usage` it is the only place a streamed call reports token counts.
- **`embedding` is a float array or a base64 string**, depending on `encoding_format` — the OpenAI
  Python SDK asks for base64 by default, so this is the common case. `AsFloats()` decodes either
  (little-endian float32).

## Steering a request, in both dialects

`InferHubCallOptions` gained `Provider` and `FleetOnly`, so `X-InferHub-Provider` reaches
`/api/chat`, `/api/generate` **and** `/v1/*` in the same release:

```csharp
await client.ChatAsync(request, InferHubCallOptions.ForFleetOnly());         // no vendor sees it
await openAi.CreateChatCompletionAsync(req, InferHubCallOptions.ForProvider("openai"));
```

A steer can only ever **narrow** what the hub's operator already configured: it cannot create a
route, and a provider that does not serve the model is refused with a `400` before anything leaves
the hub. `ForFleetOnly()` is the direction that matters — it works on a hub with four providers and
on a hub with none. Setting both spellings at once throws rather than picking a winner; the losing
intent would have been "keep this prompt off somebody else's servers".

`ForFleetOnly()` and `ForProvider(id)` are separate rather than one string field where you write
`"node"`, because the one value that means *no vendor at all* should not be spelled like a vendor id.

## `X-InferHub-Served-By`, surfaced at last

`ChatResponse`, `GenerateResponse`, `ChatCompletionResponse`, `ChatCompletionChunk` and
`CompletionResponse` all carry `ServedBy` — a node id, or `provider:<id>`. On a stream it is read
once and stamped on every chunk. It is `null` when the header is absent (`/v1/embeddings` and the
model list do not send one) rather than filled with `"unknown"`.

**Reported, never interpreted.** Nothing in this library routes, retries elsewhere or prefers on it:
re-sending a prompt to a second address is a second disclosure of that prompt.

## Errors: two dialects, two envelopes

`/v1/*` answers `{"error":{"message":…,"type":…,"param":…,"code":…}}` rather than the Ollama
surface's `{"error":"…"}`. New `InferHubOpenAiException` (a subclass, so `catch (InferHubException)`
still catches it) carries `ErrorType`, `ErrorCode` and `Param`. Before this, a refused steer
surfaced its whole JSON body as the exception message.

`ErrorCode` is always a string even when the server wrote a number — OpenAI writes
`"rate_limit_exceeded"`, OpenRouter writes `429`, and the hub keeps both parseable on its side for
the same reason.

**A `424` stays `InferHubRetrievalException` in both dialects.** Retrieval-unavailable is one
condition, and a caller should not have to catch it twice because the answer came back through
`/v1`.

## Dependencies, size, tests

- **Dependency budget unchanged**: `Microsoft.Extensions.Http` and
  `Microsoft.Extensions.DependencyInjection.Abstractions`. SSE is hand-rolled (three lines of string
  handling; `System.Net.ServerSentEvents` would be a version negotiation in every consuming app).
- `<IsAotCompatible>` still true, zero trim/AOT warnings, Release build clean with `CS1591` as error.
- **104 tests per target framework (net9.0 and net10.0): 101 pass, 3 skip** — the skips are the
  env-gated integration suite, which runs only with `INFERHUB_TEST_BASEADDRESS` set. Skipped is not
  passed. 1.0.1 had 79 per TFM.
- New sample: `dotnet/samples/OpenAiDialect` — a streamed `/v1/chat/completions` pinned to the
  fleet, printing the model list, the answer, `ServedBy` and the usage frame.

## Verified against a real hub

Every payload in the new tests was **recorded from InferHub 3.37.0** with one node driving Ollama —
the chat completion, the SSE stream including its usage frame and `[DONE]`, the legacy completion
and its stream, both embedding encodings, the model list with `capabilities`, the `404` envelope and
the refused-steer `400`. The sample was run against that hub in both directions: fleet-only (served,
`ServedBy: node`, usage reported) and a steer to a provider that hub does not have (`400
invalid_request_error`, one sentence, nothing left the hub).

## What this release does **not** establish, said out loud

- **No provider was configured on the hub used for verification**, so `ServedBy` was never observed
  reading `provider:<id>` and no vendor-served answer was parsed end to end. The refusal path is
  real; the success path through a vendor is asserted only against recorded shapes.
- **The number-shaped `error.code` test body is constructed, not recorded.** Reproducing it needs a
  configured provider *and* a real upstream rate limit. It is marked as such in the test.
- **`/v1/completions` ignores retrieval options.** The hub grounds chat, not raw completions, so an
  `InferHubCallOptions` carrying `Retrieval` reaches a hub that will not act on it. Documented in
  the XML docs rather than thrown on, because a future hub may honour it.
- **Tool calls are passed through untyped** (`JsonElement` for `tools` / `tool_choice`, and
  `function.arguments` as the JSON string the dialect sends). Typing an evolving vendor schema in a
  1.x package is a maintenance treadmill; the hub does not interpret them either.
- **`conformance/cases.json` still does not exist** — it is phase 15. The three shapes this phase
  learned are written into `spec/README.md` under "the shapes that have actually broken clients",
  which is where the corpus will pick them up.
- Audio, images, video, ingestion and the admin catch-up remain phases 9–13.

## Install

```
dotnet add package InferHub.Client --version 1.1.0
```
