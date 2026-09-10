/**
 * The admin plane (`/api/admin/*`, an admin key) and the node (a base address, not a second
 * client — root rule 6). One module because both are "the rest of the surface a caller reaches
 * with the same object", and because the node's own collection lifecycle (`/api/collections`) is
 * deliberately not the admin one (dotnet D2/D3): keeping them apart in two files would suggest a
 * relationship between the two that does not exist. Every function takes the client's own
 * `request()` (phase-21 D1), same as `_media.ts`/`_corpus.ts`.
 */

import { raiseForStatus } from "./_base.js";
import type { Requester } from "./_corpus.js";
import { readSseFrames } from "./_stream.js";
import { InferHubError } from "./errors.js";
import type {
  AdminEvent,
  AdminNode,
  ClientRow,
  CollectionDetail,
  CollectionInfo,
  CollectionsResponse,
  DeleteProfileResult,
  EnsureModelResult,
  FleetModelMatrix,
  InferHubTargetProbe,
  JsonDict,
  ModelCommandAccepted,
  NodeProfile,
  NodeProfileState,
  NodeStatusResponse,
  PutProfileResult,
  StatusResponse,
  UsageResponse,
  UsageRow,
} from "./types.js";
import type { ModelInfo } from "./types.js";

function adminNodeFromJson(data: JsonDict): AdminNode {
  const known = new Set(["nodeId", "name"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return { nodeId: (data.nodeId as string) ?? "", name: data.name as string | undefined, extra };
}

function collectionInfoFromJson(data: JsonDict): CollectionInfo {
  const known = new Set(["name", "dimension", "distance", "recordCount", "operations"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    name: (data.name as string) ?? "",
    dimension: (data.dimension as number) ?? 0,
    distance: data.distance as string | undefined,
    recordCount: data.recordCount as number | undefined,
    operations: data.operations as number | undefined,
    extra,
  };
}

function collectionsResponseFromJson(data: JsonDict): CollectionsResponse {
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (key !== "collections") extra[key] = value;
  return {
    collections: ((data.collections as JsonDict[] | undefined) ?? []).map(collectionInfoFromJson),
    extra,
  };
}

function collectionDetailFromJson(data: JsonDict): CollectionDetail {
  const known = new Set(["name", "dimension", "distance", "underReplicated"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    name: (data.name as string) ?? "",
    dimension: (data.dimension as number) ?? 0,
    distance: data.distance as string | undefined,
    underReplicated: data.underReplicated as boolean | undefined,
    extra,
  };
}

function nodeProfileFromJson(data: JsonDict): NodeProfile {
  const known = new Set(["name", "revision", "selector", "models", "maxConcurrency", "retrieval"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    name: (data.name as string) ?? "",
    revision: (data.revision as number) ?? 0,
    selector: (data.selector as JsonDict) ?? {},
    models: data.models as JsonDict | undefined,
    maxConcurrency: data.maxConcurrency as number | undefined,
    retrieval: data.retrieval as JsonDict | undefined,
    extra,
  };
}

/** `name`/`revision` are never sent — the hub sets both from the route and its own counter
 * regardless of what a caller writes there. */
function nodeProfileToJson(profile: NodeProfile): JsonDict {
  const body: JsonDict = { selector: profile.selector };
  if (profile.models !== undefined) body.models = profile.models;
  if (profile.maxConcurrency !== undefined) body.maxConcurrency = profile.maxConcurrency;
  if (profile.retrieval !== undefined) body.retrieval = profile.retrieval;
  return body;
}

function putProfileResultFromJson(data: JsonDict): PutProfileResult {
  const profile = data.profile as JsonDict | undefined;
  return {
    profile: profile !== undefined && profile !== null ? nodeProfileFromJson(profile) : undefined,
    applied: (data.applied as string[] | undefined) ?? [],
    conflicts: (data.conflicts as string[] | undefined) ?? [],
  };
}

function deleteProfileResultFromJson(data: JsonDict): DeleteProfileResult {
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (key !== "reasserted") extra[key] = value;
  return { reasserted: (data.reasserted as string[] | undefined) ?? [], extra };
}

function nodeProfileStateFromJson(data: JsonDict): NodeProfileState {
  const known = new Set(["nodeId", "desired", "effective", "refusals"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    nodeId: (data.nodeId as string) ?? "",
    desired: data.desired as JsonDict | undefined,
    effective: data.effective as JsonDict | undefined,
    refusals: (data.refusals as JsonDict[] | undefined) ?? [],
    extra,
  };
}

function modelCommandAcceptedFromJson(data: JsonDict): ModelCommandAccepted {
  const known = new Set(["nodeId", "model", "kind", "commandId", "reused"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    nodeId: (data.nodeId as string) ?? "",
    model: (data.model as string) ?? "",
    kind: (data.kind as string) ?? "",
    commandId: (data.commandId as string) ?? "",
    reused: Boolean(data.reused),
    extra,
  };
}

function fleetModelMatrixFromJson(data: JsonDict): FleetModelMatrix {
  const known = new Set(["models", "nodes"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    models: (data.models as JsonDict[] | undefined) ?? [],
    nodes: (data.nodes as JsonDict[] | undefined) ?? [],
    extra,
  };
}

function ensureModelResultFromJson(data: JsonDict): EnsureModelResult {
  const known = new Set(["satisfied", "decision"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    satisfied: Boolean(data.satisfied),
    decision: (data.decision as JsonDict) ?? {},
    extra,
  };
}

function usageRowFromJson(data: JsonDict): UsageRow {
  return {
    clientId: (data.clientId as string) ?? "",
    model: (data.model as string) ?? "",
    requests: (data.requests as number) ?? 0,
    promptTokens: (data.promptTokens as number) ?? 0,
    completionTokens: (data.completionTokens as number) ?? 0,
    totalTokens: (data.totalTokens as number) ?? 0,
    fallbackRequests: (data.fallbackRequests as number) ?? 0,
  };
}

function usageResponseFromJson(data: JsonDict | JsonDict[]): UsageResponse {
  const rows = Array.isArray(data) ? data : (data.rows as JsonDict[] | undefined);
  return { rows: (rows ?? []).map(usageRowFromJson) };
}

function clientRowFromJson(data: JsonDict): ClientRow {
  const known = new Set(["clientId", "limits", "liveUsage"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    clientId: (data.clientId as string) ?? "",
    limits: data.limits as JsonDict | undefined,
    liveUsage: data.liveUsage as JsonDict | undefined,
    extra,
  };
}

function modelInfoFromJson(data: JsonDict): ModelInfo {
  return {
    name: (data.name as string) ?? "",
    digest: data.digest as string | undefined,
    size: data.size as number | undefined,
  };
}

function nodeStatusResponseFromJson(data: JsonDict): NodeStatusResponse {
  const known = new Set([
    "mode",
    "nodeVersion",
    "nowUtc",
    "name",
    "backend",
    "concurrency",
    "gpu",
    "capabilities",
    "retrieval",
    "models",
  ]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  const backend = (data.backend ?? undefined) as JsonDict | undefined;
  const concurrency = (data.concurrency ?? undefined) as JsonDict | undefined;
  const gpu = (data.gpu ?? undefined) as JsonDict | undefined;
  const retrieval = (data.retrieval ?? undefined) as JsonDict | undefined;
  return {
    mode: (data.mode as string) ?? "solo",
    nodeVersion: data.nodeVersion as string | undefined,
    nowUtc: data.nowUtc as string | undefined,
    name: data.name as string | undefined,
    backend:
      backend !== undefined
        ? {
            name: backend.name as string | undefined,
            endpoint: backend.endpoint as string | undefined,
            health: backend.health as string | undefined,
          }
        : undefined,
    concurrency:
      concurrency !== undefined
        ? {
            limit: (concurrency.limit as number) ?? 0,
            inFlight: (concurrency.inFlight as number) ?? 0,
          }
        : undefined,
    gpu:
      gpu !== undefined
        ? {
            cuda: Boolean(gpu.cuda),
            devices: (gpu.devices as number) ?? 0,
            names: (gpu.names as string[] | undefined) ?? [],
          }
        : undefined,
    capabilities: (data.capabilities as string[] | undefined) ?? [],
    retrieval:
      retrieval !== undefined
        ? {
            enabled: Boolean(retrieval.enabled),
            provider: retrieval.provider as string | undefined,
            embeddingModel: retrieval.embeddingModel as string | undefined,
            mode: retrieval.mode as string | undefined,
            rerank: retrieval.rerank as string | undefined,
            collections: (retrieval.collections as JsonDict[] | undefined) ?? [],
            error: retrieval.error as string | undefined,
          }
        : undefined,
    models: ((data.models as JsonDict[] | undefined) ?? []).map(modelInfoFromJson),
    extra,
  };
}

const _STATUS_KNOWN_FIELDS = new Set([
  "coordinatorVersion",
  "nowUtc",
  "uptimeSeconds",
  "nodes",
  "models",
  "metrics",
  "vector",
]);

function statusResponseFromJson(data: JsonDict): StatusResponse {
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data))
    if (!_STATUS_KNOWN_FIELDS.has(key)) extra[key] = value;
  const models = data.models as JsonDict[] | undefined;
  return {
    coordinatorVersion: data.coordinatorVersion as string | undefined,
    nowUtc: data.nowUtc as string | undefined,
    uptimeSeconds: data.uptimeSeconds as number | undefined,
    nodes: data.nodes as JsonDict[] | undefined,
    models: models !== undefined ? models.map(modelInfoFromJson) : undefined,
    extra,
  };
}

/** `probe()`'s discriminator: a solo node's document carries `mode`; the hub's never does. */
function targetProbeFromJson(data: JsonDict): InferHubTargetProbe {
  if ("mode" in data) {
    const nodeStatus = nodeStatusResponseFromJson(data);
    return { kind: "solo_node", version: nodeStatus.nodeVersion, nodeStatus };
  }
  const hubStatus = statusResponseFromJson(data);
  return { kind: "hub", version: hubStatus.coordinatorVersion, hubStatus };
}

function usageQuery(
  from?: string | Date,
  to?: string | Date,
  clientId?: string,
  model?: string,
): string {
  const params = new URLSearchParams();
  if (from !== undefined) params.set("from", from instanceof Date ? from.toISOString() : from);
  if (to !== undefined) params.set("to", to instanceof Date ? to.toISOString() : to);
  if (clientId !== undefined) params.set("clientId", clientId);
  if (model !== undefined) params.set("model", model);
  const qs = params.toString();
  return qs ? `?${qs}` : "";
}

// -- Fleet ops ------------------------------------------------------------------------------------

export async function listNodes(request: Requester): Promise<AdminNode[]> {
  const response = await request("api/admin/nodes", { method: "GET" });
  await raiseForStatus(response);
  return ((await response.json()) as JsonDict[]).map(adminNodeFromJson);
}

export async function cordon(request: Requester, nodeId: string): Promise<void> {
  const response = await request(`api/admin/nodes/${nodeId}/cordon`, { method: "POST" });
  await raiseForStatus(response);
}

export async function uncordon(request: Requester, nodeId: string): Promise<void> {
  const response = await request(`api/admin/nodes/${nodeId}/uncordon`, { method: "POST" });
  await raiseForStatus(response);
}

export async function deregister(request: Requester, nodeId: string): Promise<void> {
  const response = await request(`api/admin/nodes/${nodeId}/deregister`, { method: "POST" });
  await raiseForStatus(response);
}

// -- Vector collections (admin plane) --------------------------------------------------------------

/** `GET /api/admin/vector/collections` — with replica placement. Not `listNodeCollections`:
 * different auth, different route, different shape. */
export async function listAdminCollections(request: Requester): Promise<CollectionsResponse> {
  const response = await request("api/admin/vector/collections", { method: "GET" });
  await raiseForStatus(response);
  return collectionsResponseFromJson((await response.json()) as JsonDict);
}

export async function getAdminCollection(
  request: Requester,
  collection: string,
): Promise<CollectionDetail | undefined> {
  const response = await request(`api/admin/vector/collections/${collection}`, { method: "GET" });
  if (response.status === 404) return undefined;
  await raiseForStatus(response);
  return collectionDetailFromJson((await response.json()) as JsonDict);
}

export async function createAdminCollection(
  request: Requester,
  name: string,
  dimension: number,
  distance?: string,
): Promise<CollectionInfo> {
  const body: JsonDict = { name, dimension };
  if (distance !== undefined) body.distance = distance;
  const response = await request("api/admin/vector/collections", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  await raiseForStatus(response);
  return collectionInfoFromJson((await response.json()) as JsonDict);
}

export async function dropAdminCollection(request: Requester, collection: string): Promise<void> {
  const response = await request(`api/admin/vector/collections/${collection}`, {
    method: "DELETE",
  });
  await raiseForStatus(response);
}

export async function rebuildAdminCollection(request: Requester, collection: string): Promise<void> {
  const response = await request(`api/admin/vector/collections/${collection}/rebuild`, {
    method: "POST",
  });
  await raiseForStatus(response);
}

/** `GET /api/admin/stream` (SSE) — fleet `snapshot` events and `vector.*` lifecycle events. Ends
 * when the server closes the stream. No reconnect variant: a caller that wants one wraps this
 * generator in their own retry loop. */
export async function* streamAdminEvents(request: Requester): AsyncGenerator<AdminEvent> {
  const response = await request("api/admin/stream", { method: "GET" });
  await raiseForStatus(response);
  if (!response.body) {
    throw new InferHubError(response.status, "InferHub streaming response had no body.");
  }
  for await (const { event, data } of readSseFrames(response.body)) {
    yield { event, data };
  }
}

// -- Node profiles ----------------------------------------------------------------------------------

export async function listProfiles(request: Requester): Promise<NodeProfile[]> {
  const response = await request("api/admin/profiles", { method: "GET" });
  await raiseForStatus(response);
  return ((await response.json()) as JsonDict[]).map(nodeProfileFromJson);
}

export async function getProfile(
  request: Requester,
  name: string,
): Promise<NodeProfile | undefined> {
  const response = await request(`api/admin/profiles/${name}`, { method: "GET" });
  if (response.status === 404) return undefined;
  await raiseForStatus(response);
  return nodeProfileFromJson((await response.json()) as JsonDict);
}

/** `PUT /api/admin/profiles/{name}` — creates or replaces. `profile.name`/`.revision` are
 * ignored; the hub sets both from the route and its own counter. */
export async function putProfile(
  request: Requester,
  name: string,
  profile: NodeProfile,
): Promise<PutProfileResult> {
  const response = await request(`api/admin/profiles/${name}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(nodeProfileToJson(profile)),
  });
  await raiseForStatus(response);
  return putProfileResultFromJson((await response.json()) as JsonDict);
}

export async function deleteProfile(
  request: Requester,
  name: string,
): Promise<DeleteProfileResult> {
  const response = await request(`api/admin/profiles/${name}`, { method: "DELETE" });
  await raiseForStatus(response);
  return deleteProfileResultFromJson((await response.json()) as JsonDict);
}

export async function getNodeProfile(
  request: Requester,
  nodeId: string,
): Promise<NodeProfileState> {
  const response = await request(`api/admin/nodes/${nodeId}/profile`, { method: "GET" });
  await raiseForStatus(response);
  return nodeProfileStateFromJson((await response.json()) as JsonDict);
}

// -- Model lifecycle ----------------------------------------------------------------------------------

export async function pullModel(
  request: Requester,
  nodeId: string,
  model: string,
): Promise<ModelCommandAccepted> {
  const response = await request(`api/admin/nodes/${nodeId}/models/${model}/pull`, {
    method: "POST",
  });
  await raiseForStatus(response);
  return modelCommandAcceptedFromJson((await response.json()) as JsonDict);
}

export async function deleteModel(
  request: Requester,
  nodeId: string,
  model: string,
): Promise<ModelCommandAccepted> {
  const response = await request(`api/admin/nodes/${nodeId}/models/${model}`, {
    method: "DELETE",
  });
  await raiseForStatus(response);
  return modelCommandAcceptedFromJson((await response.json()) as JsonDict);
}

export async function warmModel(
  request: Requester,
  nodeId: string,
  model: string,
): Promise<ModelCommandAccepted> {
  const response = await request(`api/admin/nodes/${nodeId}/models/${model}/warm`, {
    method: "POST",
  });
  await raiseForStatus(response);
  return modelCommandAcceptedFromJson((await response.json()) as JsonDict);
}

export async function pullToolModel(
  request: Requester,
  nodeId: string,
  tool: string,
  model: string,
): Promise<ModelCommandAccepted> {
  const response = await request(`api/admin/nodes/${nodeId}/tools/${tool}/models/${model}/pull`, {
    method: "POST",
  });
  await raiseForStatus(response);
  return modelCommandAcceptedFromJson((await response.json()) as JsonDict);
}

export async function deleteToolModel(
  request: Requester,
  nodeId: string,
  tool: string,
  model: string,
): Promise<ModelCommandAccepted> {
  const response = await request(`api/admin/nodes/${nodeId}/tools/${tool}/models/${model}`, {
    method: "DELETE",
  });
  await raiseForStatus(response);
  return modelCommandAcceptedFromJson((await response.json()) as JsonDict);
}

/** `GET /api/admin/models` — the fleet-wide model x node matrix. */
export async function listModelMatrix(request: Requester): Promise<FleetModelMatrix> {
  const response = await request("api/admin/models", { method: "GET" });
  await raiseForStatus(response);
  return fleetModelMatrixFromJson((await response.json()) as JsonDict);
}

/** `POST /api/admin/models/{model}/ensure` — pulls onto the most suitable capable-and-manageable
 * nodes that do not already have it, skipping cordoned ones. */
export async function ensureModel(
  request: Requester,
  model: string,
  replicas?: number,
): Promise<EnsureModelResult> {
  const qs = replicas !== undefined ? `?replicas=${replicas}` : "";
  const response = await request(`api/admin/models/${model}/ensure${qs}`, { method: "POST" });
  await raiseForStatus(response);
  return ensureModelResultFromJson((await response.json()) as JsonDict);
}

// -- Usage and clients ----------------------------------------------------------------------------

/** `GET /api/admin/usage` — aggregates only, never a prompt or a completion. */
export async function queryUsage(
  request: Requester,
  from?: string | Date,
  to?: string | Date,
  clientId?: string,
  model?: string,
): Promise<UsageResponse> {
  const response = await request(`api/admin/usage${usageQuery(from, to, clientId, model)}`, {
    method: "GET",
  });
  await raiseForStatus(response);
  return usageResponseFromJson((await response.json()) as JsonDict | JsonDict[]);
}

/** `GET /api/admin/clients` — never carries a key. */
export async function listClients(request: Requester): Promise<ClientRow[]> {
  const response = await request("api/admin/clients", { method: "GET" });
  await raiseForStatus(response);
  return ((await response.json()) as JsonDict[]).map(clientRowFromJson);
}

// -- The node (root rule 6 / roadmap D7): a base address, not a second client -----------------

/** `GET /api/status` — one round trip, discriminated on whether the body carries `mode` (a solo
 * node) or not (the hub — its document never has the field). */
export async function probe(request: Requester): Promise<InferHubTargetProbe> {
  const response = await request("api/status", { method: "GET" });
  await raiseForStatus(response);
  return targetProbeFromJson((await response.json()) as JsonDict);
}

/** `GET /api/version` — node-only; a 404 against a hub means "wrong target," not "wrong
 * version." */
export async function getNodeVersion(request: Requester): Promise<string> {
  const response = await request("api/version", { method: "GET" });
  await raiseForStatus(response);
  return ((await response.json()) as JsonDict).version as string;
}

/** `GET /api/collections` — node-only vector collection lifecycle; not the admin-gated
 * `/api/admin/vector/collections` (different auth, different shape, no placement/replica info —
 * a node has no fleet to place a replica on). */
export async function listNodeCollections(request: Requester): Promise<CollectionInfo[]> {
  const response = await request("api/collections", { method: "GET" });
  await raiseForStatus(response);
  const data = (await response.json()) as JsonDict;
  return ((data.collections as JsonDict[] | undefined) ?? []).map(collectionInfoFromJson);
}

export async function getNodeCollection(
  request: Requester,
  name: string,
): Promise<CollectionInfo | undefined> {
  const response = await request(`api/collections/${name}`, { method: "GET" });
  if (response.status === 404) return undefined;
  await raiseForStatus(response);
  return collectionInfoFromJson((await response.json()) as JsonDict);
}

export async function createNodeCollection(
  request: Requester,
  name: string,
  dimension: number,
  distance?: string,
): Promise<CollectionInfo> {
  const body: JsonDict = { name, dimension };
  if (distance !== undefined) body.distance = distance;
  const response = await request("api/collections", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  await raiseForStatus(response);
  return collectionInfoFromJson((await response.json()) as JsonDict);
}

export async function dropNodeCollection(request: Requester, name: string): Promise<void> {
  const response = await request(`api/collections/${name}`, { method: "DELETE" });
  await raiseForStatus(response);
}
