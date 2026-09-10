/**
 * Phase 21 — the admin plane. A small spot-check per surface, not exhaustive coverage of every
 * method (`_admin.ts`'s functions are thin, near-identical wrappers over `raiseForStatus` and one
 * fetch call, same shape `python/tests/test_admin.py` spot-checks).
 */

import { describe, expect, it, vi } from "vitest";
import { InferHubClient, InferHubError } from "../src/index.js";

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
  return new InferHubClient({ baseUrl: "http://localhost:5080/", apiKey: "sk-admin", fetch: fetchMock });
}

describe("fleet ops", () => {
  it("listNodes parses the array", async () => {
    const fetchMock = vi.fn(async () => response('[{"nodeId":"n1","name":"gpu-1"}]'));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const nodes = await client.listNodes();
    expect(nodes[0]?.nodeId).toBe("n1");
  });

  it("cordon/uncordon/deregister post to the right route", async () => {
    const fetchMock = vi.fn(async () => response(""));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    await client.cordon("n1");
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("http://localhost:5080/api/admin/nodes/n1/cordon");
    expect(init.method).toBe("POST");
  });
});

describe("admin vector collections", () => {
  it("listAdminCollections carries replica placement", async () => {
    const fetchMock = vi.fn(async () =>
      response('{"collections":[{"name":"docs","dimension":768,"recordCount":10}]}'),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.listAdminCollections();
    expect(result.collections[0]?.recordCount).toBe(10);
  });

  it("getAdminCollection returns undefined on 404", async () => {
    const fetchMock = vi.fn(async () => response('{"error":"not found"}', { status: 404 }));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    expect(await client.getAdminCollection("nope")).toBeUndefined();
  });

  it("createAdminCollection duplicate name is 409", async () => {
    const fetchMock = vi.fn(async () =>
      response('{"error":"collection already exists"}', { status: 409 }),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const error = (await client
      .createAdminCollection("docs", 768)
      .catch((e: unknown) => e)) as InferHubError;
    expect(error).toBeInstanceOf(InferHubError);
    expect(error.statusCode).toBe(409);
  });
});

describe("streamAdminEvents", () => {
  it("yields one AdminEvent per SSE frame", async () => {
    const body =
      'event: snapshot\ndata: {"nodes":1}\n\n' + 'event: model-progress\ndata: {"pct":50}\n\n';
    const fetchMock = vi.fn(async () => response(body, { contentType: "text/event-stream" }));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const events = [];
    for await (const event of client.streamAdminEvents()) events.push(event);
    expect(events).toHaveLength(2);
    expect(events[0]?.event).toBe("snapshot");
    expect(events[1]?.data).toEqual({ pct: 50 });
  });
});

describe("node profiles", () => {
  it("putProfile ignores name/revision on write", async () => {
    const fetchMock = vi.fn(async () =>
      response('{"profile":{"name":"p1","revision":2,"selector":{}},"applied":["n1"],"conflicts":[]}'),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.putProfile("p1", {
      name: "ignored",
      revision: 0,
      selector: { role: "gpu" },
      extra: {},
    });
    expect(result.applied).toEqual(["n1"]);
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const sent = JSON.parse(init.body as string);
    expect(sent.name).toBeUndefined();
    expect(sent.revision).toBeUndefined();
    expect(sent.selector).toEqual({ role: "gpu" });
  });
});

describe("model lifecycle", () => {
  it("pullModel returns the accepted command, reused surfaced", async () => {
    const fetchMock = vi.fn(async () =>
      response(
        '{"nodeId":"n1","model":"llama3","kind":"pull","commandId":"c1","reused":true}',
      ),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.pullModel("n1", "llama3");
    expect(result.reused).toBe(true);
  });

  it("ensureModel appends replicas as a query param", async () => {
    const fetchMock = vi.fn(async () => response('{"satisfied":true,"decision":{}}'));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    await client.ensureModel("llama3", 2);
    const [url] = fetchMock.mock.calls[0] as unknown as [string];
    expect(url).toBe("http://localhost:5080/api/admin/models/llama3/ensure?replicas=2");
  });
});

describe("usage and clients", () => {
  it("queryUsage builds the query string and never carries a key", async () => {
    const fetchMock = vi.fn(async () =>
      response('{"rows":[{"clientId":"c1","model":"llama3","requests":4}]}'),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.queryUsage(undefined, undefined, "c1");
    expect(result.rows[0]?.requests).toBe(4);
    const [url] = fetchMock.mock.calls[0] as unknown as [string];
    expect(url).toBe("http://localhost:5080/api/admin/usage?clientId=c1");
  });

  it("listClients never carries a key field", async () => {
    const fetchMock = vi.fn(async () => response('[{"clientId":"c1","limits":{}}]'));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const rows = await client.listClients();
    expect(rows[0]).not.toHaveProperty("key");
  });
});
