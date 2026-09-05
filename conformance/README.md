# conformance/ — one case file, every client

`cases.json` is a list of cases, each a named request (method, path, headers), the **recorded**
response a real hub returned, and an `assert` block naming the outcome a correct client must
produce. It is **data, and no language owns it** — an OpenAPI document was considered and rejected:
a schema says what is well-formed, and every bug that has actually reached a client here is about
what is *correct* — that a comma-separated `X-InferHub-Sources` must still parse, that a `424` is a
different exception from a `404`, that a mid-stream `{"error":…,"done":true}` must terminate rather
than hang.

**Phase 15 shipped 13 cases** — not the whole list `spec/README.md` carries under "The shapes that
have actually broken clients", which stays as the map for what a later phase promotes next. Every
recorded body in `cases.json` was copied from an existing `dotnet/tests/InferHub.Client.Tests`
literal (itself captured from a real hub or, where that test file says so, derived from the hub's
own serializer) — nothing here was typed from a schema.

**Phase 15's founding case, and why it satisfies "the runner is green and the client failed a case
found by reading the hub, not the client":** `node-status-rerank-is-a-string`. `dotnet/v1.7.0`'s
`NodeStatusResponse.Retrieval.Rerank` was typed `bool?`, guessed with no live node reachable to check
against. Installing the published `1.7.0` from nuget.org and driving a real solo node threw a
`JsonException` — the real hub sends a string. That is a case a client got wrong, found by running
against a real hub rather than by inspecting the client's own code, one release before this phase
formally started. It is recorded here as case #1 so no other language repeats it; the fix shipped as
`dotnet/v1.7.1`.

## The case schema

```json
{
  "id": "kebab-case-name",
  "kind": "chat | chat-stream | probe | openai-chat | openai-chat-stream | openai-images-submit | ingest-text | search | chunks",
  "description": "why this shape breaks a naive client, in one or two sentences",
  "request": { "method": "GET|POST|...", "path": "/api/..." },
  "response": { "status": 200, "headers": {}, "body": "...", "mediaType": "application/json" },
  "assert": { "kind": "...", "...": "case-kind-specific fields the runner checks" }
}
```

`kind` picks which client method a runner drives; `assert.kind` picks which outcome it checks. A
runner that does not yet implement a `kind` throws `NotSupportedException` naming it — loud, not a
silently-skipped case. The C# runner is the reference implementation of the switch; a second
language's runner reads the same file and needs the same `kind`s, not the same code.

## Running it

```
dotnet test dotnet/InferHub.Client.sln --filter FullyQualifiedName~ConformanceCorpusTests
```

The runner (`dotnet/tests/InferHub.Client.Tests/ConformanceCorpusTests.cs`) walks up from the test
binary's output directory to find this folder, so the corpus is read once, from the repository root,
never copied into a language's test project.

## Adding a case

Found a shape that broke a client (or would have)? Add it to `spec/README.md`'s list first if it
isn't there, then to `cases.json` with a body copied from a real recorded response — never
hand-typed — then extend the runner's `switch` if its `kind` is new. **A behaviour discovered in one
language becomes a case before it becomes a fix**, or the other languages ship the same bug.
