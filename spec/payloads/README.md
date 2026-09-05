# spec/payloads/ — response bodies recorded from a real hub

**Empty on purpose, and this file is the reason.**

Phase 7 planned to move the recorded payloads out of `dotnet/tests` and into this folder, on the
assumption that they were files. They are not: every one of them is an **inline C# string literal**
inside `InferHubClientTests.cs`, `InferHubAdminClientTests.cs` and the three handler fakes. Moving
them is not a `git mv` — it is an edit to eight test files, and phase 7's own acceptance test is
that the same 85 tests pass **unchanged**.

**Recorded deviation from the phase-7 plan above.** Phase 15 built the corpus as
`conformance/cases.json` — each case carries its recorded `response.body` inline rather than as a
separate file in this folder. A one-file-per-payload layout earns its keep once several languages'
runners need to fetch the same bytes independently; with one runner (C#) so far, a second copy in
this folder would be exactly the drift the corpus exists to prevent — the JSON file *is* the
extraction, verified by the runner rather than by whoever pasted it. This folder stays empty and
reserved: if a case's body becomes large enough to make `cases.json` unwieldy (a full SSE transcript,
a multi-KB job document), it moves here and `cases.json` references it by filename — a decision for
whichever phase first needs it, not one to take pre-emptively.

The C# test files (`InferHubClientTests.cs`, `InferHubOpenAiClientTests.cs`, and five more) still
carry their own recorded literals for the cases phase 15 did not promote to the shared corpus — that
extraction is unfinished, tracked in `conformance/README.md`, not silently abandoned.
