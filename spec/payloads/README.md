# spec/payloads/ — response bodies recorded from a real hub

**Empty on purpose, and this file is the reason.**

Phase 7 planned to move the recorded payloads out of `dotnet/tests` and into this folder, on the
assumption that they were files. They are not: every one of them is an **inline C# string literal**
inside `InferHubClientTests.cs`, `InferHubAdminClientTests.cs` and the three handler fakes. Moving
them is not a `git mv` — it is an edit to eight test files, and phase 7's own acceptance test is
that the same 85 tests pass **unchanged**.

So the extraction belongs to **phase 15**, where the conformance corpus is built and where an
extracted payload is verified by a runner rather than by whoever pasted it. Doing it here would
produce a second copy of every literal with nothing checking that the two agree — which is exactly
the drift the corpus exists to prevent.

**What goes here, from phase 15 on:** one file per recorded response, named for the case that uses
it, captured from a running InferHub and never hand-written. A payload somebody typed is a payload
that agrees with what its author believed, which is the one thing a corpus must not do (15 D2).
