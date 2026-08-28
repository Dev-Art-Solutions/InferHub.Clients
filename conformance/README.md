# conformance/ — one case file, every client

**Empty until phase 15**, and the folder exists so the pointer in the root `CLAUDE.md` resolves to
something rather than teaching a reader that the idea does not exist.

## What lands here

`cases.json` — a list of cases, each a named request (method, path, headers, body), the **recorded**
response a real hub returned, and the assertions a correct client must satisfy: which fields it must
expose, which header it must send, which error type it must raise. Then a thin runner per language
(~150 lines) that drives its own client against a stub server built from the file.

The corpus is **data, and no language owns it**. An OpenAPI document was considered and rejected: a
schema says what is well-formed, and every bug that has actually reached a client here is about what
is *correct* — that a comma-separated `X-InferHub-Sources` must still parse, that a `424` is a
different exception from a `404`, that a mid-stream `{"error":…,"done":true}` must terminate rather
than hang, that content is read-once.

**Phase 15 is finished when the C# client fails a case.** A corpus written from an implementation
and validated against that same implementation asserts nothing; if no such case is found, the corpus
was written by copying the client and the phase has not happened.

Candidate cases are already listed under "The shapes that have actually broken clients" in
`spec/README.md`.
