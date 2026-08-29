# Social copy — dotnet/v1.1.0 (Iliya posts these manually)

Verify the facts at posting time, not from memory: that 1.1.0 is on nuget.org, that the parity
table's OpenAI row reads ✓ for C#, and that `dotnet/samples/OpenAiDialect` runs against the hub you
point it at.

## Facebook

> InferHub.Client 1.1.0 is out, and it adds the thing a caller actually asks for twice: the
> OpenAI dialect, and a way to say where a prompt may go.
>
> `IInferHubOpenAiClient` covers /v1/chat/completions, /v1/completions, /v1/embeddings and
> /v1/models — same hub, same address, same key as the Ollama surface. Streaming is real SSE:
> [DONE] ends it, and the frame with an empty `choices` array is the usage frame, which is the
> only place a streamed call reports token counts. Embeddings come back as floats or as base64
> depending on what you asked for, and one call decodes either.
>
> The half that matters more: `X-InferHub-Provider` now reaches both dialects.
> `InferHubCallOptions.ForFleetOnly()` keeps a prompt on your own machines — on a hub with four
> cloud providers configured and on a hub with none — and `ForProvider("openai")` steers to one
> that is already configured, or is refused with a 400 before anything leaves the hub. A header
> can never create a route your configuration does not contain.
>
> And every answer now carries ServedBy: the node id, or provider:<id>. The client reports it and
> does nothing about it — re-sending a prompt to a second address is a second disclosure of that
> prompt, and that is not a decision a client library gets to make quietly.
>
> Additive: nothing from 1.0 changed. 104 tests per target framework, two dependencies, still
> AOT-clean.
>
> Package: nuget.org/packages/InferHub.Client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

> InferHub.Client 1.1.0 — the OpenAI dialect (/v1/chat/completions, /v1/completions,
> /v1/embeddings, /v1/models) plus the provider steer.
>
> ForFleetOnly() keeps a prompt off every vendor. ForProvider(id) picks one your config already
> allows, or gets a 400. ServedBy is surfaced and never acted on.
>
> Additive. Two dependencies. nuget.org/packages/InferHub.Client

## Notes for the blog post

Slug: `inferhub-client-openai-dialect-provider-steer`. `list_posts` first, then create it
**visible in one shot** — the connector is insert-only with a locking slug, so a draft you meant
to fix is a post you cannot fix. **No shell commands in the HTML**: the Cloudflare WAF blocks the
*request*, not the command, so show the C# and the JSON envelope rather than a `curl` or a
`dotnet add package` line inside a `<pre>`. The post lands at
`blog.devart.solutions/blog/inferhub-client-openai-dialect-provider-steer`.

Angle: the steer, not the dialect. "Second dialect" is table stakes and every SDK has one; the
interesting claim is that a caller — not just the operator — can say *node* and keep one prompt on
their own hardware, and that the hub refuses a steer it cannot honour instead of quietly serving it
from somewhere else. Two good details to show: the refusal is deliberately the **same sentence**
for an unknown provider, a disabled one and a real one that maps a different model (so a client
with an inference key cannot enumerate the operator's vendors by probing), and the usage frame with
an empty `choices` array, which is the kind of thing you only learn by recording a real stream.

Worth one honest line: the verification hub had no cloud provider configured, so the refusal path
is proven end to end and the vendor-served path is proven only against recorded shapes.
