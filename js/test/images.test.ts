/**
 * Phase 21 — images: the synchronous /v1/images/* routes and the /api/images/jobs seam. Payloads
 * mirror `python/tests/test_images.py`.
 */

import { describe, expect, it, vi } from "vitest";
import { InferHubClient, InferHubOpenAiException } from "../src/index.js";

function response(
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

describe("generateImage / editImage / createImageVariation", () => {
  it("parses the envelope and sends option headers", async () => {
    const fetchMock = vi.fn(async () =>
      response(JSON.stringify({ created: 1, data: [{ b64_json: "AAA=", seed: 42 }] })),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.generateImage({
      model: "sdxl",
      prompt: "a lighthouse in fog",
      options: { steps: 28, seed: 42 },
    });
    expect(result.data[0]?.b64Json).toBe("AAA=");
    expect(result.data[0]?.seed).toBe(42);

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const headers = init.headers as Record<string, string>;
    expect(headers["X-InferHub-Image-Steps"]).toBe("28");
    expect(headers["X-InferHub-Image-Seed"]).toBe("42");
  });

  it("edit is multipart with no operation field", async () => {
    const fetchMock = vi.fn(async () => response('{"created":1,"data":[]}'));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    await client.editImage({
      model: "sdxl",
      image: new Blob([new Uint8Array([1])]),
      imageFilename: "a.png",
      prompt: "brighter",
    });
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const form = init.body as FormData;
    expect(form.has("operation")).toBe(false);
    expect(form.get("prompt")).toBe("brighter");
  });

  it("variation has no prompt field", async () => {
    const fetchMock = vi.fn(async () => response('{"created":1,"data":[]}'));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    await client.createImageVariation({
      model: "sdxl",
      image: new Blob([new Uint8Array([1])]),
      imageFilename: "a.png",
    });
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const form = init.body as FormData;
    expect(form.has("prompt")).toBe(false);
  });
});

describe("the job seam", () => {
  it("submitImageGeneration carries operation=generate", async () => {
    const fetchMock = vi.fn(async () =>
      response('{"id":"job-1","state":"queued","capability":"image"}'),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const job = await client.submitImageGeneration({ model: "sdxl", prompt: "p" });
    expect(job.id).toBe("job-1");
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const sent = JSON.parse(init.body as string);
    expect(sent.operation).toBe("generate");
  });

  it("submitImageEdit/Variation are multipart with operation", async () => {
    const fetchMock1 = vi.fn(async () => response('{"id":"job-2","state":"queued"}'));
    const client1 = clientWith(fetchMock1 as unknown as typeof fetch);
    await client1.submitImageEdit({
      model: "sdxl",
      image: new Blob([new Uint8Array([1])]),
      imageFilename: "a.png",
      prompt: "p",
    });
    const [, init1] = fetchMock1.mock.calls[0] as unknown as [string, RequestInit];
    expect((init1.body as FormData).get("operation")).toBe("edit");

    const fetchMock2 = vi.fn(async () => response('{"id":"job-3","state":"queued"}'));
    const client2 = clientWith(fetchMock2 as unknown as typeof fetch);
    await client2.submitImageVariation({
      model: "sdxl",
      image: new Blob([new Uint8Array([1])]),
      imageFilename: "a.png",
    });
    const [, init2] = fetchMock2.mock.calls[0] as unknown as [string, RequestInit];
    expect((init2.body as FormData).get("operation")).toBe("variation");
  });

  it("listImageJobs parses the recorded empty listing", async () => {
    const body =
      '{"jobs":[],"queued":0,"active":0,"retainedBytes":0,"retentionSeconds":300,"persistence":"none"}';
    const fetchMock = vi.fn(async () => response(body));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.listImageJobs();
    expect(result.jobs).toEqual([]);
    expect(result.retentionSeconds).toBe(300);
    expect(result.persistence).toBe("none");
  });

  it("getImageJob returns undefined on 404", async () => {
    const fetchMock = vi.fn(async () => response('{"error":"not found"}', { status: 404 }));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    expect(await client.getImageJob("nope")).toBeUndefined();
  });

  it("watchImageJob stops at a terminal state", async () => {
    const body =
      'data: {"id":"j1","state":"running","capability":"image","step":1,"totalSteps":2}\n\n' +
      'data: {"id":"j1","state":"succeeded","capability":"image","step":2,"totalSteps":2,' +
      '"images":[{"index":0,"url":"/api/images/jobs/j1/content/0"}]}\n\n';
    const fetchMock = vi.fn(async () => response(body, { contentType: "text/event-stream" }));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const frames = [];
    for await (const frame of client.watchImageJob("j1")) frames.push(frame);
    expect(frames).toHaveLength(2);
    expect(frames[1]?.state).toBe("succeeded");
    expect(frames[1]?.images[0]?.url).toBe("/api/images/jobs/j1/content/0");
  });

  it("openImageContent carries projection headers", async () => {
    const fetchMock = vi.fn(async () =>
      response("\x89PNG", {
        contentType: "image/png",
        headers: { "X-InferHub-Image-Projection": "equirectangular" },
      }),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const content = await client.openImageContent("j1", 0);
    expect(content.projection).toBe("equirectangular");
  });

  it("openImageContent 410 job_expired throws InferHubOpenAiException", async () => {
    const body =
      '{"error":{"message":"this image has already been read","type":"api_error","param":null,"code":"job_expired"}}';
    const fetchMock = vi.fn(async () => response(body, { status: 410 }));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const error = (await client
      .openImageContent("j1", 0)
      .catch((e: unknown) => e)) as InferHubOpenAiException;
    expect(error).toBeInstanceOf(InferHubOpenAiException);
    expect(error.statusCode).toBe(410);
    expect(error.errorCode).toBe("job_expired");
  });

  it("cancelImageJob returns the job as it actually ended", async () => {
    const fetchMock = vi.fn(async () =>
      response('{"id":"j1","state":"succeeded","capability":"image"}'),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const job = await client.cancelImageJob("j1");
    expect(job.state).toBe("succeeded");
  });

  it("submitImageGeneration 503 capability_unavailable carries Retry-After", async () => {
    const body =
      '{"error":{"message":"no node currently provides \'image\' for model \'llava:latest\'",' +
      '"type":"api_error","param":null,"code":"capability_unavailable"}}';
    const fetchMock = vi.fn(async () =>
      response(body, { status: 503, headers: { "Retry-After": "30" } }),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const error = (await client
      .submitImageGeneration({ model: "llava:latest", prompt: "x" })
      .catch((e: unknown) => e)) as InferHubOpenAiException;
    expect(error.retryAfter).toBe(30);
  });
});
