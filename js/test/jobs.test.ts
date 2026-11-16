/**
 * Text jobs (hub v3.103+). The status bodies and the result headers are **recorded** from a real
 * coordinator and a real node over Ollama (qwen2.5:0.5b) on 2026-11-16. The `group` body is
 * **derived** from the hub's serializer (`JobEndpoints.Describe`) — no split-model group was
 * running when these were recorded — and so is the SSE framing.
 */

import { describe, expect, it, vi } from "vitest";
import { InferHubClient, InferHubError } from "../src/index.js";

const ID = "8755d357-7f7a-493b-984b-09eeb1c3797e";

const QUEUED =
  '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b",' +
  '"status":"queued","stage":"waiting","createdAt":"2026-11-16T11:40:16.2677184+00:00",' +
  '"updatedAt":"2026-11-16T11:40:16.2677184+00:00","attempts":0,"pieces":0}';

const PREPARING =
  '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b",' +
  '"status":"running","stage":"preparing","createdAt":"2026-11-16T11:40:16.2677184+00:00",' +
  '"startedAt":"2026-11-16T11:40:16.2950626+00:00","updatedAt":"2026-11-16T11:40:18.3304721+00:00",' +
  '"node":{"id":"d2b53940-e3b4-4b37-855d-536e9a4fe10e","name":"live150-node",' +
  '"reportedAt":"2026-11-16T11:40:18.3304721+00:00","pieces":0},"attempts":1,"pieces":0}';

const SUCCEEDED =
  '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b",' +
  '"status":"succeeded","stage":"done","createdAt":"2026-11-16T11:40:16.2677184+00:00",' +
  '"startedAt":"2026-11-16T11:40:16.2950626+00:00","updatedAt":"2026-11-16T11:40:33.342876+00:00",' +
  '"completedAt":"2026-11-16T11:40:33.342876+00:00","node":{"id":"d2b53940-e3b4-4b37-855d-536e9a4fe10e",' +
  '"name":"live150-node","reportedAt":"2026-11-16T11:40:32.4983246+00:00","pieces":1},' +
  '"attempts":1,"pieces":300,"tokens":300,"tokensPerSecond":340.23}';

const PARTIAL_RESULT = '{"model":"qwen2.5:0.5b","message":{"role":"assistant","content":""},"done":false}';

const RESULT =
  '{"model":"qwen2.5:0.5b","created_at":"2026-11-16T11:40:33.3396733Z",' +
  '"message":{"role":"assistant","content":"Ah, the lighthouse keeper."},"done":true,' +
  '"done_reason":"length","eval_count":300}';

// Derived, not recorded.
const GROUP =
  '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"wizardlm-70b-split",' +
  '"status":"running","stage":"preparing","detail":"group starting: 18420 of 25022 MiB sent to its members",' +
  '"attempts":1,"pieces":0,"group":{"name":"wizardlm-70b-split","head":"shard-a","ready":false,' +
  '"state":"starting","members":[{"node":"shard-a","layers":27},' +
  '{"node":"shard-b","layers":27,"expectMiB":12511,"sentMiB":9210},' +
  '{"node":"shard-c","layers":27,"expectMiB":12511,"sentMiB":9210}]}}';

const FAILED =
  '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"llama3","status":"failed",' +
  '"stage":"done","attempts":1,"pieces":4,"error":{"status":502,"code":"node_lost",' +
  '"message":"node \'gpu-1\' disappeared while this job was running; it was not retried"}}';

function response(body: string | null, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(body, { status, headers: { "content-type": "application/json", ...headers } });
}

function clientWith(fetchMock: unknown): InferHubClient {
  return new InferHubClient({ baseUrl: "http://localhost:5080/", fetch: fetchMock as typeof fetch });
}

describe("text jobs", () => {
  it("submit wraps the chat request and resolves with the status at once", async () => {
    const fetchMock = vi.fn(async () => response(QUEUED, 202));
    const job = await clientWith(fetchMock).submitChatJob({
      model: "qwen2.5:0.5b",
      messages: [{ role: "user", content: "hi" }],
    });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const sent = JSON.parse(init.body as string);
    expect(String(url)).toContain("/api/jobs");
    expect(init.method).toBe("POST");
    expect(sent.kind).toBe("chat");
    expect(sent.request.model).toBe("qwen2.5:0.5b");
    expect(sent.request.messages[0].content).toBe("hi");
    expect(job.id).toBe(ID);
    expect(job.status).toBe("queued");
    expect(job.stage).toBe("waiting");
    expect(job.isTerminal).toBe(false);
    expect(job.node).toBeUndefined();
  });

  it("submit generate names its kind", async () => {
    const fetchMock = vi.fn(async () => response(QUEUED, 202));
    await clientWith(fetchMock).submitGenerateJob({ model: "qwen2.5:0.5b", prompt: "hi" });
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(JSON.parse(init.body as string).kind).toBe("generate");
  });

  it("a running status carries the node's own report", async () => {
    const job = await clientWith(vi.fn(async () => response(PREPARING))).getJob(ID);
    expect(job?.status).toBe("running");
    expect(job?.stage).toBe("preparing");
    expect(job?.node?.name).toBe("live150-node");
    expect(job?.node?.reportedAt).toBe("2026-11-16T11:40:18.3304721+00:00");
  });

  it("a finished status has exact tokens", async () => {
    const job = await clientWith(vi.fn(async () => response(SUCCEEDED))).getJob(ID);
    expect(job?.isTerminal).toBe(true);
    expect(job?.tokens).toBe(300);
    expect(job?.tokensPerSecond).toBe(340.23);
  });

  it("a split model's status says how far each member is", async () => {
    const job = await clientWith(vi.fn(async () => response(GROUP))).getJob(ID);
    expect(job?.detail).toMatch(/^group starting/);
    expect(job?.group?.state).toBe("starting");
    expect(job?.group?.ready).toBe(false);
    expect(job?.group?.members.map((m) => m.node)).toEqual(["shard-a", "shard-b", "shard-c"]);
    expect(job?.group?.members[0]?.expectMiB).toBeUndefined();
    expect(job?.group?.members[1]?.sentMiB).toBe(9210);
    expect(job?.group?.loadedFraction).toBeCloseTo(18420 / 25022, 5);

    // Found against a real group: once it runs, the count says nothing about a load.
    const running = await clientWith(
      vi.fn(async () => response(GROUP.replace('"state":"starting"', '"state":"running"'))),
    ).getJob(ID);
    expect(running?.group?.loadedFraction).toBeUndefined();
  });

  it("an unknown job is undefined, not an error", async () => {
    const client = clientWith(vi.fn(async () => response('{"error":"job not found"}', 404)));
    expect(await client.getJob(ID)).toBeUndefined();
  });

  it("the result of a running job is the text so far and not done", async () => {
    const result = await clientWith(vi.fn(async () => response(PARTIAL_RESULT, 202))).getJobResult(ID);
    expect(result.done).toBe(false);
    expect(result.text).toBe("");
  });

  it("the result of a finished job is the chat answer with who served it", async () => {
    const fetchMock = vi.fn(async () => response(RESULT, 200, { "X-InferHub-Served-By": "node" }));
    const result = await clientWith(fetchMock).getJobResult(ID);
    expect(String((fetchMock.mock.calls[0] as unknown as [string])[0])).toContain(`/api/jobs/${ID}/result`);
    expect(result.done).toBe(true);
    expect(result.text).toBe("Ah, the lighthouse keeper.");
    expect(result.chat?.evalCount).toBe(300);
    expect(result.servedBy).toBe("node");
    expect(result.generate).toBeUndefined();
  });

  it("the result of a failed job throws its own status", async () => {
    const client = clientWith(vi.fn(async () => response('{"error":"node \'gpu-1\' disappeared"}', 502)));
    await expect(client.getJobResult(ID)).rejects.toMatchObject({ statusCode: 502 });
  });

  it("a failed status is read, not thrown", async () => {
    const job = await clientWith(vi.fn(async () => response(FAILED))).getJob(ID);
    expect(job?.status).toBe("failed");
    expect(job?.error?.code).toBe("node_lost");
    expect(job?.error?.status).toBe(502);
  });

  it("cancel resolves with the status; dropping a finished job resolves undefined", async () => {
    const fetchMock = vi.fn(async () => response(FAILED.replace('"failed"', '"cancelled"'), 202));
    const cancelled = await clientWith(fetchMock).cancelJob(ID);
    expect(cancelled?.status).toBe("cancelled");
    expect((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].method).toBe("DELETE");

    expect(await clientWith(vi.fn(async () => response(null, 204))).cancelJob(ID)).toBeUndefined();
  });

  it("list reads the envelope", async () => {
    const client = clientWith(vi.fn(async () => response(`{"jobs":[${SUCCEEDED},${PREPARING}]}`)));
    expect((await client.listJobs()).map((j) => j.status)).toEqual(["succeeded", "running"]);
  });

  it("watch yields each status and stops at the terminal one", async () => {
    const stream =
      `event: status\ndata: ${PREPARING}\n\n: waiting\n\n` +
      `event: status\ndata: ${SUCCEEDED}\n\nevent: status\ndata: ${QUEUED}\n\n`;
    const fetchMock = vi.fn(async () => response(stream, 200, { "content-type": "text/event-stream" }));
    const seen: string[] = [];
    for await (const job of clientWith(fetchMock).watchJob(ID)) seen.push(job.status);
    expect(seen).toEqual(["running", "succeeded"]);
  });

  it("wait polls until the job is finished", async () => {
    const bodies = [QUEUED, PREPARING, SUCCEEDED];
    const fetchMock = vi.fn(async () => response(bodies.length > 1 ? bodies.shift()! : bodies[0]!));
    const job = await clientWith(fetchMock).waitForJob(ID, { pollIntervalMs: 0 });
    expect(job.status).toBe("succeeded");
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("wait times out while the job keeps running, and a job that is gone throws a 404", async () => {
    const running = clientWith(vi.fn(async () => response(PREPARING)));
    await expect(running.waitForJob(ID, { pollIntervalMs: 0, timeoutMs: 0 })).rejects.toThrow(/still running/);

    const gone = clientWith(vi.fn(async () => response('{"error":"job not found"}', 404)));
    const error = await gone.waitForJob(ID, { pollIntervalMs: 0 }).catch((e) => e);
    expect(error).toBeInstanceOf(InferHubError);
    expect((error as InferHubError).statusCode).toBe(404);
  });
});
