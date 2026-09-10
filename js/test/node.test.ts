/**
 * Phase 21 — the node: a base address, not a second client (root rule 6). `probe()` and the
 * five node-only methods, driven against the same recorded bodies the conformance corpus uses.
 */

import { describe, expect, it, vi } from "vitest";
import { InferHubClient, InferHubError } from "../src/index.js";

const NODE_STATUS_BODY =
  '{"mode":"solo","nodeVersion":"3.37.0","nowUtc":"2026-09-05T00:00:00Z","name":"local-node",' +
  '"backend":{"name":"ollama","endpoint":"http://host.docker.internal:11434/","health":null},' +
  '"concurrency":null,"gpu":{"cuda":false,"devices":0,"names":[]},' +
  '"capabilities":["chat","embed"],"retrieval":{"enabled":true,"provider":"local",' +
  '"embeddingModel":"nomic-embed-text","mode":"vector","rerank":"none","collections":[]},' +
  '"models":[]}';

const HUB_STATUS_BODY =
  '{"coordinatorVersion":"3.37.0","nowUtc":"2026-09-05T00:00:00Z","uptimeSeconds":12.5,' +
  '"nodes":[{"nodeId":"n1","name":"gpu-1"}],"models":[{"name":"llama3"}]}';

function response(body: string, status = 200): Response {
  return new Response(body, { status, headers: { "content-type": "application/json" } });
}

function clientWith(fetchMock: typeof fetch): InferHubClient {
  return new InferHubClient({ baseUrl: "http://localhost:5080/", fetch: fetchMock });
}

describe("probe", () => {
  it("discriminates on the mode field — solo node", async () => {
    const fetchMock = vi.fn(async () => response(NODE_STATUS_BODY));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.probe();
    expect(result.kind).toBe("solo_node");
    expect(result.version).toBe("3.37.0");
    expect(result.nodeStatus?.name).toBe("local-node");
    // The founding conformance case: rerank is a string mode, never a bool.
    expect(result.nodeStatus?.retrieval?.rerank).toBe("none");
    expect(typeof result.nodeStatus?.retrieval?.rerank).toBe("string");
    expect(result.hubStatus).toBeUndefined();
  });

  it("discriminates on the mode field — hub", async () => {
    const fetchMock = vi.fn(async () => response(HUB_STATUS_BODY));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const result = await client.probe();
    expect(result.kind).toBe("hub");
    expect(result.version).toBe("3.37.0");
    expect(result.hubStatus?.nodes).toHaveLength(1);
    expect(result.nodeStatus).toBeUndefined();
  });
});

describe("node-only surface", () => {
  it("getNodeVersion reads the literal field", async () => {
    const fetchMock = vi.fn(async () => response('{"version":"3.37.0"}'));
    const client = clientWith(fetchMock as unknown as typeof fetch);
    expect(await client.getNodeVersion()).toBe("3.37.0");
  });

  it("node collections lifecycle", async () => {
    const fetchMock = vi.fn(async () =>
      response(
        '{"collections":[{"name":"docs","dimension":768,"distance":"cosine",' +
          '"recordCount":412,"operations":890}]}',
      ),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const collections = await client.listNodeCollections();
    expect(collections[0]?.name).toBe("docs");
    expect(collections[0]?.recordCount).toBe(412);

    const missingFetch = vi.fn(async () => response('{"error":"not found"}', 404));
    const missingClient = clientWith(missingFetch as unknown as typeof fetch);
    expect(await missingClient.getNodeCollection("nope")).toBeUndefined();

    const createFetch = vi.fn(async () =>
      response('{"name":"new-docs","dimension":768,"distance":"cosine"}'),
    );
    const createClient = clientWith(createFetch as unknown as typeof fetch);
    const created = await createClient.createNodeCollection("new-docs", 768, "cosine");
    expect(created.name).toBe("new-docs");

    const dropFetch = vi.fn(async () => response(""));
    const dropClient = clientWith(dropFetch as unknown as typeof fetch);
    await dropClient.dropNodeCollection("new-docs");
  });

  it("hits /api/collections, a different route from admin collections", async () => {
    const fetchMock = vi.fn(async () =>
      response('{"collections":[{"name":"docs","dimension":768}]}'),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    await client.listNodeCollections();
    const [url] = fetchMock.mock.calls[0] as unknown as [string];
    expect(url).toBe("http://localhost:5080/api/collections");
  });

  it("createNodeCollection duplicate name is 409", async () => {
    const fetchMock = vi.fn(async () =>
      response('{"error":"collection already exists"}', 409),
    );
    const client = clientWith(fetchMock as unknown as typeof fetch);
    const error = (await client
      .createNodeCollection("docs", 768)
      .catch((e: unknown) => e)) as InferHubError;
    expect(error).toBeInstanceOf(InferHubError);
    expect(error.statusCode).toBe(409);
  });
});
