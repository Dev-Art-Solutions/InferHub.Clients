/**
 * Phase 15/19 — drives `conformance/cases.json` against `InferHubClient`. Same file the C# and
 * Python runners read. A case whose `kind` is outside `js/v0.1.0`'s surface (probe, the OpenAI
 * dialect, retrieval/ingestion/search) is skipped with a named reason rather than silently
 * omitted (D5) — mirrors python 16's `_SUPPORTED_KINDS` split.
 */

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { describe, expect, it } from "vitest";
import {
  InferHubClient,
  InferHubError,
  InferHubOpenAiException,
  InferHubRetrievalException,
} from "../src/index.js";

interface ConformanceCase {
  id: string;
  kind: string;
  description: string;
  request: { method: string; path: string };
  response: { status: number; headers?: Record<string, string>; mediaType?: string; body: string };
  assert: Record<string, unknown>;
}

function findCasesFile(): string {
  let dir = path.dirname(fileURLToPath(import.meta.url));
  for (let i = 0; i < 10; i++) {
    const candidate = path.join(dir, "conformance", "cases.json");
    try {
      readFileSync(candidate);
      return candidate;
    } catch {
      // keep walking up
    }
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  throw new Error("conformance/cases.json not found above " + fileURLToPath(import.meta.url));
}

const cases: ConformanceCase[] = JSON.parse(readFileSync(findCasesFile(), "utf-8")).cases;

// js/v1.0.0 (phase 21) adds probe() and the images job seam's OpenAI envelope — the OpenAI chat
// dialect (openai-chat/openai-chat-stream) stays dotnet-only per roadmap D3.
const SUPPORTED_KINDS = new Set([
  "chat",
  "chat-stream",
  "ingest-text",
  "search",
  "chunks",
  "probe",
  "openai-images-submit",
]);

function clientFor(testCase: ConformanceCase): InferHubClient {
  const { response } = testCase;
  const fetchMock = async () =>
    new Response(response.body, {
      status: response.status,
      headers: {
        "content-type": response.mediaType ?? "application/json",
        ...(response.headers ?? {}),
      },
    });
  return new InferHubClient({
    baseUrl: "http://localhost:5080/",
    fetch: fetchMock as unknown as typeof fetch,
  });
}

describe("conformance corpus", () => {
  for (const testCase of cases) {
    const { kind } = testCase;
    const skipReason = !SUPPORTED_KINDS.has(kind)
      ? `'${kind}' is outside inferhub-client v1.0.0's surface (the OpenAI chat dialect stays ` +
        `dotnet-only per roadmap-polyglot-clients D3)`
      : undefined;
    const name = skipReason ? `${testCase.id} (skip: ${skipReason})` : testCase.id;

    it(name, async (ctx) => {
      if (skipReason) {
        // named skip (D5) — not a silent filter of the case list; the reason is in the test name
        // (vitest surfaces it in the reporter) and via ctx.skip() so it counts as skipped, not passed.
        ctx.skip();
        return;
      }

      const assertKind = testCase.assert.kind as string;
      const client = clientFor(testCase);
      const request = { model: "llama3", messages: [] };

      if (assertKind === "throws-retrieval-exception") {
        await expect(client.chat(request)).rejects.toBeInstanceOf(InferHubRetrievalException);
        const error = await client.chat(request).catch((e) => e);
        expect(error.statusCode).toBe(424);
        return;
      }

      if (assertKind === "source-ids") {
        const result = await client.chat(request);
        expect(result.sourceIds).toEqual(testCase.assert.expected);
        return;
      }

      if (assertKind === "stream-terminal-error") {
        const seen: unknown[] = [];
        await expect(async () => {
          for await (const chunk of client.chatStream(request)) {
            seen.push(chunk);
          }
        }).rejects.toBeInstanceOf(InferHubError);
        expect(seen).toHaveLength(testCase.assert.partialChunks as number);
        return;
      }

      if (assertKind === "ingest-partial-returned") {
        const result = await client.ingestText("handbook", { id: "z", text: "x" });
        expect(result.documentId).toBe(testCase.assert.documentId);
        expect(result.chunksEmbedded).toBe(testCase.assert.chunksEmbedded);
        return;
      }

      if (assertKind === "hits-in-wire-order") {
        const result = await client.search("handbook", "q");
        expect(result.hits[0]?.documentId).toBe(testCase.assert.firstDocumentId);
        expect(result.hits[1]?.documentId).toBe(testCase.assert.secondDocumentId);
        return;
      }

      if (assertKind === "chunk-index-string") {
        const result = await client.getChunks("handbook", "onboarding");
        expect(result.chunks[0]?.index).toBe(testCase.assert.expected);
        expect(typeof result.chunks[0]?.index).toBe("string");
        return;
      }

      if (assertKind === "solo-node") {
        const result = await client.probe();
        expect(result.kind).toBe("solo_node");
        expect(result.nodeStatus?.name).toBe(testCase.assert.nodeName);
        expect(result.nodeStatus?.retrieval?.rerank).toBe(testCase.assert.retrievalRerank);
        return;
      }

      if (assertKind === "hub") {
        const result = await client.probe();
        expect(result.kind).toBe("hub");
        expect(result.hubStatus?.nodes).toHaveLength(testCase.assert.nodeCount as number);
        return;
      }

      if (assertKind === "throws-openai-exception" && kind === "openai-images-submit") {
        const error = await client
          .submitImageGeneration({ model: "llava:latest", prompt: "x" })
          .catch((e) => e);
        expect(error).toBeInstanceOf(InferHubOpenAiException);
        expect(error.errorCode).toBe(testCase.assert.errorCode);
        expect(error.retryAfter).toBe(testCase.assert.retryAfterSeconds);
        return;
      }

      throw new Error(`assert.kind '${assertKind}' has no runner for kind '${kind}' yet`);
    });
  }
});

describe("conformance corpus — coverage", () => {
  it("skips every case outside v1.0.0's surface by name, not silently", () => {
    const skipped = cases.filter((c) => !SUPPORTED_KINDS.has(c.kind));
    const covered = cases.filter((c) => SUPPORTED_KINDS.has(c.kind));
    // 13 cases total: 10 in v1.0.0's surface (chat/chat-stream + ingest-text/search/chunks +
    // probe x2 + openai-images-submit), 3 outside it (the OpenAI chat dialect, dotnet-only).
    expect(covered.length + skipped.length).toBe(cases.length);
    expect(covered.length).toBe(10);
    expect(skipped.length).toBe(3);
  });
});
