/**
 * Text jobs — `/api/jobs` (hub v3.103+). A chat or a generate submitted as a job: the hub answers
 * at once with an id, runs it the way `/api/chat` would, and keeps a status the caller, the node
 * running it and the hub all update. For an answer that takes minutes — a big model, a model split
 * across nodes, a full fleet — where one held connection is the wrong shape.
 *
 * A separate module like `_corpus.ts`: its own shapes, a two-status result (`200` done, `202` not
 * yet) and an SSE status stream. **Hub only** — a solo node has no `/api/jobs`.
 */

import { raiseForStatus, readServedBy } from "./_base.js";
import { readSseFrames } from "./_stream.js";
import { InferHubError } from "./errors.js";
import type { ChatResponse, GenerateResponse, JsonDict } from "./types.js";
import type { Requester } from "./_corpus.js";

/** The node a job was sent to. `reportedAt` is when that node last said it is working on the job —
 * it reports every couple of seconds, so a stale value means the node went quiet, not the model.
 * Absent from a node older than v3.103. */
export interface JobNode {
  id: string;
  name?: string;
  reportedAt?: string;
  pieces?: number;
}

/** One member of a split model's group: the layers it holds and how much of its share of the model
 * the head has sent it (`sentMiB` of `expectMiB`) — the load, while a group starts. */
export interface JobGroupMember {
  node: string;
  layers?: number;
  expectMiB?: number;
  sentMiB?: number;
}

/** Present when the job's model is one model split across several nodes. */
export interface JobGroup {
  name: string;
  head?: string;
  ready: boolean;
  state?: string;
  why?: string;
  members: JobGroupMember[];
  /** How much of the members' shares has been sent, 0–1, while the group is coming up. `undefined`
   * once the group runs (a member that kept its layers from an earlier load is sent only a part of
   * its share, so the number means nothing then) and when the group does not say. */
  loadedFraction?: number;
}

/** Why a job failed: the HTTP status the blocking route would have answered, a code (`refused`,
 * `node_error`, `node_lost`, `deadline`, `cancelled`) and the sentence. */
export interface JobError {
  status?: number;
  code?: string;
  message?: string;
  retryAfterSeconds?: number;
}

export type JobStatus = "queued" | "running" | "succeeded" | "failed" | "cancelled";

/**
 * A job's status: names, counts and times — never the request or the answer. `stage` is `waiting`
 * (at the hub; `detail` says for what), `sent`, `preparing` (the node has it and its engine has
 * produced nothing yet), `generating` or `done`. `pieces` counts the pieces of the answer received
 * so far; `tokens` is exact and arrives with the last one.
 */
export interface Job {
  id: string;
  kind: string;
  model: string;
  status: JobStatus | string;
  stage: string;
  detail?: string;
  createdAt?: string;
  startedAt?: string;
  updatedAt?: string;
  completedAt?: string;
  node?: JobNode;
  servedBy?: string;
  attempts: number;
  pieces: number;
  tokens?: number;
  tokensPerSecond?: number;
  group?: JobGroup;
  error?: JobError;
  client?: string;
  /** `succeeded`, `failed` or `cancelled`. */
  isTerminal: boolean;
  extra: JsonDict;
}

/** A job's answer. `done` is `false` while the job still runs — `chat`/`generate` then hold the
 * text so far, which is what a caller that reconnects reads. Exactly one of them is set. */
export interface JobResult {
  done: boolean;
  chat?: ChatResponse;
  generate?: GenerateResponse;
  /** The answer's text, whichever kind the job is. */
  text: string;
  servedBy?: string;
  raw: JsonDict;
}

export interface WaitForJobOptions {
  /** Milliseconds between polls. Default 1000. */
  pollIntervalMs?: number;
  /** Give up waiting after this many milliseconds (the job keeps running). */
  timeoutMs?: number;
  signal?: AbortSignal;
}

const KNOWN = new Set([
  "id", "kind", "model", "status", "stage", "detail", "createdAt", "startedAt", "updatedAt",
  "completedAt", "node", "servedBy", "attempts", "pieces", "tokens", "tokensPerSecond", "group",
  "error", "client",
]);

const TERMINAL = new Set(["succeeded", "failed", "cancelled"]);

function groupFromJson(data: JsonDict): JobGroup {
  const members = ((data.members as JsonDict[] | undefined) ?? []).map((m) => ({
    node: (m.node as string) ?? "",
    layers: m.layers as number | undefined,
    expectMiB: m.expectMiB as number | undefined,
    sentMiB: m.sentMiB as number | undefined,
  }));
  const expect = members.reduce((sum, m) => sum + (m.expectMiB ?? 0), 0);
  const sent = members.reduce((sum, m) => sum + Math.min(m.sentMiB ?? 0, m.expectMiB ?? 0), 0);
  return {
    name: (data.name as string) ?? "",
    head: data.head as string | undefined,
    ready: Boolean(data.ready),
    state: data.state as string | undefined,
    why: data.why as string | undefined,
    members,
    loadedFraction: expect > 0 && data.state !== "running" ? sent / expect : undefined,
  };
}

export function jobFromJson(data: JsonDict): Job {
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) {
    if (!KNOWN.has(key)) extra[key] = value;
  }
  const node = data.node as JsonDict | undefined;
  const error = data.error as JsonDict | undefined;
  const status = (data.status as string) ?? "";
  return {
    id: (data.id as string) ?? "",
    kind: (data.kind as string) ?? "",
    model: (data.model as string) ?? "",
    status,
    stage: (data.stage as string) ?? "",
    detail: data.detail as string | undefined,
    createdAt: data.createdAt as string | undefined,
    startedAt: data.startedAt as string | undefined,
    updatedAt: data.updatedAt as string | undefined,
    completedAt: data.completedAt as string | undefined,
    node: node
      ? {
          id: (node.id as string) ?? "",
          name: node.name as string | undefined,
          reportedAt: node.reportedAt as string | undefined,
          pieces: node.pieces as number | undefined,
        }
      : undefined,
    servedBy: data.servedBy as string | undefined,
    attempts: (data.attempts as number) ?? 0,
    pieces: (data.pieces as number) ?? 0,
    tokens: data.tokens as number | undefined,
    tokensPerSecond: data.tokensPerSecond as number | undefined,
    group: data.group ? groupFromJson(data.group as JsonDict) : undefined,
    error: error
      ? {
          status: error.status as number | undefined,
          code: error.code as string | undefined,
          message: error.message as string | undefined,
          retryAfterSeconds: error.retryAfterSeconds as number | undefined,
        }
      : undefined,
    client: data.client as string | undefined,
    isTerminal: TERMINAL.has(status),
    extra,
  };
}

const JSON_HEADERS = { "Content-Type": "application/json" };

export async function submitJob(request: Requester, kind: "chat" | "generate", body: JsonDict): Promise<Job> {
  const response = await request("api/jobs", {
    method: "POST",
    headers: JSON_HEADERS,
    body: JSON.stringify({ kind, request: body }),
  });
  await raiseForStatus(response);
  return jobFromJson((await response.json()) as JsonDict);
}

export async function getJob(request: Requester, id: string): Promise<Job | undefined> {
  const response = await request(`api/jobs/${encodeURIComponent(id)}`);
  if (response.status === 404) return undefined;
  await raiseForStatus(response);
  return jobFromJson((await response.json()) as JsonDict);
}

export async function listJobs(request: Requester): Promise<Job[]> {
  const response = await request("api/jobs");
  await raiseForStatus(response);
  const data = (await response.json()) as JsonDict;
  return ((data.jobs as JsonDict[] | undefined) ?? []).map(jobFromJson);
}

/** The raw result: the body, whether the job is done (`200`) or still running (`202`), and who
 * served it. A failed job answers its own error status and a cancelled one a 409 — both throw. */
export async function getJobResultRaw(
  request: Requester,
  id: string,
): Promise<{ done: boolean; servedBy?: string; raw: JsonDict }> {
  const response = await request(`api/jobs/${encodeURIComponent(id)}/result`);
  await raiseForStatus(response);
  return {
    done: response.status === 200,
    servedBy: readServedBy(response),
    raw: (await response.json()) as JsonDict,
  };
}

/** `202` + the status: a running job was asked to stop. `204`: a finished job was dropped. */
export async function cancelJob(request: Requester, id: string): Promise<Job | undefined> {
  const response = await request(`api/jobs/${encodeURIComponent(id)}`, { method: "DELETE" });
  if (response.status === 204 || response.status === 404) return undefined;
  await raiseForStatus(response);
  return jobFromJson((await response.json()) as JsonDict);
}

export async function* watchJob(request: Requester, id: string): AsyncGenerator<Job> {
  const response = await request(`api/jobs/${encodeURIComponent(id)}/events`, {
    headers: { Accept: "text/event-stream" },
  });
  await raiseForStatus(response);
  if (!response.body) return;
  for await (const frame of readSseFrames(response.body)) {
    const job = jobFromJson(frame.data as JsonDict);
    yield job;
    if (job.isTerminal) return;
  }
}

export async function waitForJob(
  request: Requester,
  id: string,
  options: WaitForJobOptions = {},
): Promise<Job> {
  const interval = options.pollIntervalMs ?? 1000;
  const deadline = options.timeoutMs === undefined ? undefined : Date.now() + options.timeoutMs;
  for (;;) {
    options.signal?.throwIfAborted();
    const job = await getJob(request, id);
    if (!job) {
      throw new InferHubError(
        404,
        `job '${id}' not found — it finished and its retention passed, the hub restarted, or it was never this client's`,
      );
    }
    if (job.isTerminal) return job;
    if (deadline !== undefined && Date.now() >= deadline) {
      throw new Error(`job '${id}' is still ${job.status} after ${options.timeoutMs} ms`);
    }
    await new Promise((resolve) => setTimeout(resolve, interval));
  }
}
