/**
 * Phase 21 — audio: transcription (parsed and verbatim) and speech (buffered and streamed).
 * Payloads mirror `python/tests/test_audio.py`'s recorded/derived bodies, so a divergence between
 * the two clients on the same wire is a test failure in one of them, not a coincidence.
 */

import { describe, expect, it, vi } from "vitest";
import { InferHubClient, InferHubOpenAiException } from "../src/index.js";

function textResponse(
  body: string,
  init: { status?: number; headers?: Record<string, string>; contentType?: string } = {},
): Response {
  return new Response(body, {
    status: init.status ?? 200,
    headers: { "content-type": init.contentType ?? "application/json", ...(init.headers ?? {}) },
  });
}

function clientWith(fetchMock: typeof fetch): InferHubClient {
  return new InferHubClient({ baseUrl: "http://localhost:5080/", fetch: fetchMock });
}

describe("transcribe / transcribeDocument", () => {
  it("forces verbose_json and parses segments", async () => {
    const body = JSON.stringify({
      text: "hello world",
      language: "en",
      duration: 1.2,
      segments: [{ id: 0, start: 0.0, end: 1.2, text: "hello world" }],
    });
    const fetchMock = vi.fn(async () => textResponse(body));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.transcribe({
      model: "whisper",
      audio: new Blob([new Uint8Array([1, 2, 3])]),
      filename: "a.wav",
      responseFormat: "text",
    });
    expect(result.text).toBe("hello world");
    expect(result.segments[0]?.text).toBe("hello world");

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const form = init.body as FormData;
    expect(form.get("response_format")).toBe("verbose_json");
    // The file part is last (dotnet D5 — every field before the file, always).
    const keys: string[] = [];
    for (const [key] of form as unknown as Iterable<[string, unknown]>) keys.push(key);
    expect(keys.indexOf("file")).toBeGreaterThan(keys.indexOf("model"));
  });

  it("returns the document unaltered", async () => {
    const srt = "1\n00:00:00,000 --> 00:00:01,000\nhello\n\n";
    const fetchMock = vi.fn(async () => textResponse(srt, { contentType: "text/plain" }));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.transcribeDocument({
      model: "whisper",
      audio: new Blob([new Uint8Array([1])]),
      filename: "a.wav",
      responseFormat: "srt",
    });
    expect(result.content).toContain("hello");
    expect(result.contentType).toContain("text/plain");
  });
});

describe("createSpeech / streamSpeech", () => {
  it("returns the response with headers stamped", async () => {
    const fetchMock = vi.fn(async () =>
      textResponse("RIFF....WAVEfmt ", {
        contentType: "audio/wav",
        headers: { "X-InferHub-Served-By": "node-1", "X-InferHub-Audio-Sample-Rate": "22050" },
      }),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.createSpeech({ model: "piper", input: "hi", responseFormat: "wav" });
    expect(result.sampleRate).toBe(22050);
    expect(result.servedBy).toBe("node-1");
  });

  it("yields a delta then a terminal zero-usage frame", async () => {
    const audioB64 = btoa("\x00\x01");
    const body =
      `event: speech.audio.delta\ndata: {"type":"speech.audio.delta","audio":"${audioB64}"}\n\n` +
      'event: speech.audio.done\ndata: {"type":"speech.audio.done","usage":' +
      '{"input_tokens":0,"output_tokens":0,"total_tokens":0}}\n\n';
    const fetchMock = vi.fn(async () =>
      textResponse(body, {
        contentType: "text/event-stream",
        headers: { "X-InferHub-Served-By": "node-1", "X-InferHub-Speech-Characters": "2" },
      }),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const chunks = [];
    for await (const chunk of client.streamSpeech({ model: "piper", input: "hi" })) {
      chunks.push(chunk);
    }
    expect(chunks).toHaveLength(2);
    expect(chunks[0]?.characters).toBe(2);
    // The terminal frame's three zeros are a true count, not a placeholder (dotnet D3).
    expect(chunks[1]?.type).toBe("speech.audio.done");
    expect(chunks[1]?.usage).toEqual({ input_tokens: 0, output_tokens: 0, total_tokens: 0 });
    expect(chunks[1]?.audio).toBeUndefined();
  });

  it("throws on an error frame", async () => {
    const body =
      'event: speech.audio.error\ndata: {"error":{"message":"node dropped",' +
      '"code":"capability_unavailable"}}\n\n';
    const fetchMock = vi.fn(async () => textResponse(body, { contentType: "text/event-stream" }));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    await expect(async () => {
      for await (const _chunk of client.streamSpeech({ model: "piper", input: "hi" })) {
        // draining
      }
    }).rejects.toBeInstanceOf(InferHubOpenAiException);
  });

  it("carries Retry-After on a 503 capability_unavailable", async () => {
    const body =
      '{"error":{"message":"no node currently provides \'speak\' for model \'gemma:2b\'",' +
      '"type":"api_error","param":null,"code":"capability_unavailable"}}';
    const fetchMock = vi.fn(async () =>
      textResponse(body, { status: 503, headers: { "Retry-After": "30" } }),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const error = (await client
      .createSpeech({ model: "gemma:2b", input: "hi" })
      .catch((e: unknown) => e)) as InferHubOpenAiException;
    expect(error).toBeInstanceOf(InferHubOpenAiException);
    expect(error.statusCode).toBe(503);
    expect(error.errorCode).toBe("capability_unavailable");
    expect(error.retryAfter).toBe(30);
  });
});
