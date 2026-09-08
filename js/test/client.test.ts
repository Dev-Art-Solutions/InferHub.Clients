/**
 * Mocked-`fetch` unit tests for `InferHubClient` — no test in this repository calls a live hub
 * (root `CLAUDE.md` testing-discipline rule). `conformance.test.ts` drives the shared corpus;
 * this file covers request shaping, streaming mechanics and error mapping the corpus does not.
 */

import { describe, expect, it, vi } from "vitest";
import {
  InferHubClient,
  InferHubError,
  InferHubOpenAiException,
  InferHubRetrievalException,
} from "../src/index.js";

function jsonResponse(
  body: unknown,
  init: { status?: number; headers?: Record<string, string> } = {},
): Response {
  return new Response(JSON.stringify(body), {
    status: init.status ?? 200,
    headers: { "content-type": "application/json", ...(init.headers ?? {}) },
  });
}

function ndjsonResponse(
  lines: string[],
  init: { status?: number; headers?: Record<string, string> } = {},
): Response {
  const body = lines.join("\n");
  return new Response(body, {
    status: init.status ?? 200,
    headers: { "content-type": "application/x-ndjson", ...(init.headers ?? {}) },
  });
}

describe("InferHubClient — request shaping", () => {
  it("sends an Authorization header when an apiKey is given", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ models: [{ name: "llama3" }] }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      apiKey: "sk-client-token-1",
      fetch: fetchMock as unknown as typeof fetch,
    });
    await client.listModels();
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect((init.headers as Record<string, string>).Authorization).toBe(
      "Bearer sk-client-token-1",
    );
  });

  it("omits Authorization when no apiKey is given", async () => {
    const fetchMock = vi.fn(async () => jsonResponse({ models: [] }));
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    await client.listModels();
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect((init.headers as Record<string, string>).Authorization).toBeUndefined();
  });

  it("resolves relative paths against baseUrl regardless of a trailing slash", async () => {
    const fetchMock = vi.fn(async () => jsonResponse({ models: [] }));
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080",
      fetch: fetchMock as unknown as typeof fetch,
    });
    await client.listModels();
    const [url] = fetchMock.mock.calls[0] as unknown as [string];
    expect(url).toBe("http://localhost:5080/api/tags");
  });

  it("chat() sends stream:false and returns a parsed ChatResponse", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({
        model: "llama3",
        message: { role: "assistant", content: "hi there" },
        done: true,
        eval_count: 12,
      }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const result = await client.chat({
      model: "llama3",
      messages: [{ role: "user", content: "hi" }],
    });
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://localhost:5080/api/chat");
    const sentBody = JSON.parse(init.body as string);
    expect(sentBody.stream).toBe(false);
    expect(sentBody.messages).toEqual([{ role: "user", content: "hi" }]);
    expect(result.message?.content).toBe("hi there");
    expect(result.evalCount).toBe(12);
    expect(result.done).toBe(true);
  });

  it("chat() keeps unknown response fields in extra", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({
        model: "llama3",
        message: { role: "assistant", content: "hi" },
        done: true,
        a_future_field: 42,
      }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const result = await client.chat({ model: "llama3", messages: [] });
    expect(result.extra).toEqual({ a_future_field: 42 });
  });

  it("chat() merges extra into the request body and passes options/format/keepAlive", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ model: "llama3", message: { role: "assistant", content: "" }, done: true }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    await client.chat({
      model: "llama3",
      messages: [],
      options: { temperature: 0.2 },
      format: "json",
      keepAlive: "5m",
      extra: { tools: [{ type: "function" }] },
    });
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const sentBody = JSON.parse(init.body as string);
    expect(sentBody.options).toEqual({ temperature: 0.2 });
    expect(sentBody.format).toBe("json");
    expect(sentBody.keep_alive).toBe("5m");
    expect(sentBody.tools).toEqual([{ type: "function" }]);
  });

  it("generate() sends stream:false and returns a parsed GenerateResponse", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ model: "llama3", response: "hello", done: true, context: [1, 2, 3] }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const result = await client.generate({ model: "llama3", prompt: "hi" });
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const sentBody = JSON.parse(init.body as string);
    expect(sentBody.stream).toBe(false);
    expect(result.response).toBe("hello");
    expect(result.context).toEqual([1, 2, 3]);
  });
});

describe("InferHubClient — streaming", () => {
  it("chatStream() yields one ChatResponse per NDJSON line and stops at done:true", async () => {
    const fetchMock = vi.fn(async () =>
      ndjsonResponse([
        JSON.stringify({ model: "llama3", message: { role: "assistant", content: "a" }, done: false }),
        JSON.stringify({ model: "llama3", message: { role: "assistant", content: "b" }, done: false }),
        JSON.stringify({ model: "llama3", message: { role: "assistant", content: "" }, done: true }),
      ]),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const chunks = [];
    for await (const chunk of client.chatStream({ model: "llama3", messages: [] })) {
      chunks.push(chunk);
    }
    expect(chunks).toHaveLength(3);
    expect(chunks[0]?.message?.content).toBe("a");
    expect(chunks[2]?.done).toBe(true);

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(init.body as string).stream).toBe(true);
  });

  it("chatStream() buffers a line split across chunks", async () => {
    const encoder = new TextEncoder();
    const fullLine = JSON.stringify({
      model: "llama3",
      message: { role: "assistant", content: "hello" },
      done: true,
    });
    const half = Math.floor(fullLine.length / 2);
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(encoder.encode(fullLine.slice(0, half)));
        controller.enqueue(encoder.encode(fullLine.slice(half) + "\n"));
        controller.close();
      },
    });
    const fetchMock = vi.fn(
      async () =>
        new Response(stream, {
          status: 200,
          headers: { "content-type": "application/x-ndjson" },
        }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const chunks = [];
    for await (const chunk of client.chatStream({ model: "llama3", messages: [] })) {
      chunks.push(chunk);
    }
    expect(chunks).toHaveLength(1);
    expect(chunks[0]?.message?.content).toBe("hello");
  });

  it("chatStream() throws InferHubError on a mid-stream terminal error and does not hang", async () => {
    const fetchMock = vi.fn(async () =>
      ndjsonResponse([
        JSON.stringify({ model: "llama3", message: { role: "assistant", content: "partial" }, done: false }),
        JSON.stringify({ error: "node dropped mid-stream", done: true }),
      ]),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const seen: unknown[] = [];
    await expect(async () => {
      for await (const chunk of client.chatStream({ model: "llama3", messages: [] })) {
        seen.push(chunk);
      }
    }).rejects.toThrow(InferHubError);
    expect(seen).toHaveLength(1);
  });

  it("generateStream() yields chunks and stops at done:true", async () => {
    const fetchMock = vi.fn(async () =>
      ndjsonResponse([
        JSON.stringify({ model: "llama3", response: "a", done: false }),
        JSON.stringify({ model: "llama3", response: "b", done: true }),
      ]),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const chunks = [];
    for await (const chunk of client.generateStream({ model: "llama3", prompt: "hi" })) {
      chunks.push(chunk);
    }
    expect(chunks).toHaveLength(2);
    expect(chunks[1]?.done).toBe(true);
  });
});

describe("InferHubClient — headers surfaced, never interpreted", () => {
  it("chat() reads X-InferHub-Served-By and X-InferHub-Sources (JSON array)", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse(
        { model: "llama3", message: { role: "assistant", content: "hi" }, done: true },
        { headers: { "X-InferHub-Served-By": "node-1", "X-InferHub-Sources": '["doc-1","doc-2"]' } },
      ),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const result = await client.chat({ model: "llama3", messages: [] });
    expect(result.servedBy).toBe("node-1");
    expect(result.sourceIds).toEqual(["doc-1", "doc-2"]);
  });

  it("chat() falls back to comma-separated X-InferHub-Sources", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse(
        { model: "llama3", message: { role: "assistant", content: "hi" }, done: true },
        { headers: { "X-InferHub-Sources": "doc-1,doc-2" } },
      ),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const result = await client.chat({ model: "llama3", messages: [] });
    expect(result.sourceIds).toEqual(["doc-1", "doc-2"]);
  });
});

describe("InferHubClient — errors", () => {
  it("throws InferHubRetrievalException on 424", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ error: "retrieval unavailable" }, { status: 424 }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    await expect(client.chat({ model: "llama3", messages: [] })).rejects.toBeInstanceOf(
      InferHubRetrievalException,
    );
  });

  it("throws InferHubOpenAiException for the {error:{...}} envelope, carrying code/param", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse(
        {
          error: {
            message: "no such model",
            type: "invalid_request_error",
            param: "model",
            code: "model_not_found",
          },
        },
        { status: 400 },
      ),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    try {
      await client.chat({ model: "llama3", messages: [] });
      expect.unreachable();
    } catch (error) {
      expect(error).toBeInstanceOf(InferHubOpenAiException);
      expect((error as InferHubOpenAiException).errorCode).toBe("model_not_found");
      expect((error as InferHubOpenAiException).param).toBe("model");
    }
  });

  it("throws InferHubError for the plain {error:\"...\"} envelope", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ error: "model 'x' not found" }, { status: 404 }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    try {
      await client.chat({ model: "x", messages: [] });
      expect.unreachable();
    } catch (error) {
      expect(error).toBeInstanceOf(InferHubError);
      expect(error).not.toBeInstanceOf(InferHubOpenAiException);
      expect((error as InferHubError).statusCode).toBe(404);
      expect((error as InferHubError).message).toBe("model 'x' not found");
    }
  });

  it("captures retryAfter from a Retry-After header", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse(
        { error: { message: "busy", type: "api_error", param: null, code: "capability_unavailable" } },
        { status: 503, headers: { "Retry-After": "30" } },
      ),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    try {
      await client.chat({ model: "llama3", messages: [] });
      expect.unreachable();
    } catch (error) {
      expect((error as InferHubError).retryAfter).toBe(30);
    }
  });

  it("an InferHubRetrievalException is still caught by an InferHubError handler", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ error: "retrieval unavailable" }, { status: 424 }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    await expect(client.chat({ model: "llama3", messages: [] })).rejects.toBeInstanceOf(
      InferHubError,
    );
  });
});

describe("InferHubClient — embeddings, status, ping", () => {
  it("embed() returns the parsed vectors", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ model: "nomic-embed-text", embeddings: [[0.1, 0.2]] }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const result = await client.embed({ model: "nomic-embed-text", input: "hello" });
    expect(result.embeddings).toEqual([[0.1, 0.2]]);
  });

  it("embed() throws InferHubError on an empty vector list", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ model: "nomic-embed-text", embeddings: [] }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    await expect(
      client.embed({ model: "nomic-embed-text", input: "hello" }),
    ).rejects.toBeInstanceOf(InferHubError);
  });

  it("embedLegacy() returns the parsed vector", async () => {
    const fetchMock = vi.fn(async () => jsonResponse({ embedding: [0.5, 0.6] }));
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const result = await client.embedLegacy({ model: "nomic-embed-text", prompt: "hello" });
    expect(result.embedding).toEqual([0.5, 0.6]);
  });

  it("getStatus() parses the hub shape and keeps unknown fields in extra", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({
        coordinatorVersion: "3.37.0",
        nowUtc: "2026-09-05T00:00:00Z",
        uptimeSeconds: 12.5,
        nodes: [{ nodeId: "n1", name: "gpu-1" }],
        models: [{ name: "llama3" }],
        somethingNew: true,
      }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const status = await client.getStatus();
    expect(status.coordinatorVersion).toBe("3.37.0");
    expect(status.nodes).toHaveLength(1);
    expect(status.models?.[0]?.name).toBe("llama3");
    expect(status.extra).toEqual({ somethingNew: true });
  });

  it("ping() returns true on 2xx and false otherwise, never throwing", async () => {
    const okFetch = vi.fn(async () => new Response(null, { status: 200 }));
    const okClient = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: okFetch as unknown as typeof fetch,
    });
    await expect(okClient.ping()).resolves.toBe(true);

    const downFetch = vi.fn(async () => new Response(null, { status: 503 }));
    const downClient = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: downFetch as unknown as typeof fetch,
    });
    await expect(downClient.ping()).resolves.toBe(false);
  });

  it("listModels() parses TagsResponse", async () => {
    const fetchMock = vi.fn(async () =>
      jsonResponse({ models: [{ name: "llama3", digest: "abc", size: 123 }] }),
    );
    const client = new InferHubClient({
      baseUrl: "http://localhost:5080/",
      fetch: fetchMock as unknown as typeof fetch,
    });
    const result = await client.listModels();
    expect(result.models).toEqual([{ name: "llama3", digest: "abc", size: 123 }]);
  });
});
