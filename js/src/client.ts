/**
 * `InferHubClient` — one class, `fetch`-based, no sync/async split (D1). `chat`/`generate` return
 * `Promise<T>`; `chatStream`/`generateStream` return `AsyncIterable<T>`. Covers the Ollama-dialect
 * core surface only: chat, generate (blocking + streaming), embeddings, model listing,
 * status/health. Retrieval and modalities/admin/node are later phases (`js/v0.2.0`, `js/v1.0.0`).
 */

import {
  buildHeaders,
  buildRetrievalHeaders,
  DEFAULT_BASE_URL,
  parseNdjsonLine,
  raiseForStatus,
  readServedBy,
  readSourceIds,
} from "./_base.js";
import * as corpus from "./_corpus.js";
import { readNdjsonLines } from "./_stream.js";
import { InferHubError } from "./errors.js";
import type {
  ChatMessage,
  ChatRequest,
  ChatResponse,
  DocumentChunksResponse,
  DocumentDeletion,
  DocumentSummary,
  EmbeddingsRequest,
  EmbeddingsResponse,
  EmbedRequest,
  EmbedResponse,
  FileDocument,
  GenerateRequest,
  GenerateResponse,
  IngestResult,
  InferHubClientOptions,
  JsonDict,
  ModelInfo,
  RetrievalOptions,
  SearchRequest,
  SearchResponse,
  StatusResponse,
  TagsResponse,
  TextDocument,
  VectorMatch,
  VectorQuery,
  VectorRecord,
  VectorUpsert,
} from "./types.js";

// -- Wire (de)serialization ---------------------------------------------------------------------
// Ollama's dialect is snake_case on the wire; every public type here is camelCase, idiomatic
// TypeScript (D4). Object-rest destructuring is what builds `extra` — everything not named in a
// shape's known-field list falls into the rest, without a Python-style field-name set to maintain.

function messageToJson(message: ChatMessage): JsonDict {
  const body: JsonDict = { role: message.role, content: message.content ?? "" };
  if (message.images !== undefined) {
    body.images = message.images;
  }
  if (message.toolCalls !== undefined) {
    body.tool_calls = message.toolCalls;
  }
  Object.assign(body, message.extra ?? {});
  return body;
}

function messageFromJson(data: JsonDict): ChatMessage {
  const { role, content, images, tool_calls, ...rest } = data as {
    role?: string;
    content?: string;
    images?: string[];
    tool_calls?: JsonDict[];
    [key: string]: unknown;
  };
  return {
    role: role ?? "",
    content: content ?? "",
    images,
    toolCalls: tool_calls,
    extra: rest,
  };
}

function chatRequestToJson(request: ChatRequest, stream: boolean): JsonDict {
  const body: JsonDict = {
    model: request.model,
    messages: request.messages.map(messageToJson),
    stream,
  };
  if (request.options !== undefined) body.options = request.options;
  if (request.format !== undefined) body.format = request.format;
  if (request.keepAlive !== undefined) body.keep_alive = request.keepAlive;
  Object.assign(body, request.extra ?? {});
  return body;
}

function chatResponseFromJson(data: JsonDict): ChatResponse {
  const {
    model,
    created_at,
    message,
    done,
    done_reason,
    total_duration,
    load_duration,
    prompt_eval_count,
    prompt_eval_duration,
    eval_count,
    eval_duration,
    error,
    ...rest
  } = data as Record<string, unknown>;
  return {
    model: (model as string) ?? "",
    createdAt: created_at as string | undefined,
    message:
      message !== undefined && message !== null
        ? messageFromJson(message as JsonDict)
        : undefined,
    done: done as boolean | undefined,
    doneReason: done_reason as string | undefined,
    totalDuration: total_duration as number | undefined,
    loadDuration: load_duration as number | undefined,
    promptEvalCount: prompt_eval_count as number | undefined,
    promptEvalDuration: prompt_eval_duration as number | undefined,
    evalCount: eval_count as number | undefined,
    evalDuration: eval_duration as number | undefined,
    error: error as string | undefined,
    extra: rest,
  };
}

function generateRequestToJson(request: GenerateRequest, stream: boolean): JsonDict {
  const body: JsonDict = {
    model: request.model,
    prompt: request.prompt ?? "",
    stream,
  };
  if (request.options !== undefined) body.options = request.options;
  if (request.format !== undefined) body.format = request.format;
  if (request.keepAlive !== undefined) body.keep_alive = request.keepAlive;
  Object.assign(body, request.extra ?? {});
  return body;
}

function generateResponseFromJson(data: JsonDict): GenerateResponse {
  const {
    model,
    created_at,
    response,
    done,
    done_reason,
    context,
    total_duration,
    load_duration,
    prompt_eval_count,
    prompt_eval_duration,
    eval_count,
    eval_duration,
    error,
    ...rest
  } = data as Record<string, unknown>;
  return {
    model: (model as string) ?? "",
    createdAt: created_at as string | undefined,
    response: (response as string) ?? "",
    done: done as boolean | undefined,
    doneReason: done_reason as string | undefined,
    context: context as number[] | undefined,
    totalDuration: total_duration as number | undefined,
    loadDuration: load_duration as number | undefined,
    promptEvalCount: prompt_eval_count as number | undefined,
    promptEvalDuration: prompt_eval_duration as number | undefined,
    evalCount: eval_count as number | undefined,
    evalDuration: eval_duration as number | undefined,
    error: error as string | undefined,
    extra: rest,
  };
}

function modelInfoFromJson(data: JsonDict): ModelInfo {
  return {
    name: (data.name as string) ?? "",
    digest: data.digest as string | undefined,
    size: data.size as number | undefined,
  };
}

function tagsResponseFromJson(data: JsonDict): TagsResponse {
  const models = (data.models as JsonDict[] | undefined) ?? [];
  return { models: models.map(modelInfoFromJson) };
}

function statusResponseFromJson(data: JsonDict): StatusResponse {
  const { coordinatorVersion, nowUtc, uptimeSeconds, nodes, models, metrics, vector, ...rest } =
    data as Record<string, unknown>;
  void metrics;
  void vector; // known-but-unmodeled fields, excluded from `extra` same as python's field list
  return {
    coordinatorVersion: coordinatorVersion as string | undefined,
    nowUtc: nowUtc as string | undefined,
    uptimeSeconds: uptimeSeconds as number | undefined,
    nodes: nodes as JsonDict[] | undefined,
    models: models !== undefined ? (models as JsonDict[]).map(modelInfoFromJson) : undefined,
    extra: rest,
  };
}

function vectorUpsertToJson(upsert: VectorUpsert): JsonDict {
  const body: JsonDict = { id: upsert.id };
  if (upsert.vector !== undefined) body.vector = upsert.vector;
  if (upsert.text !== undefined) body.text = upsert.text;
  if (upsert.payload !== undefined) body.payload = upsert.payload;
  return body;
}

function vectorQueryToJson(query: VectorQuery): JsonDict {
  const body: JsonDict = { topK: query.topK ?? 10 };
  if (query.vector !== undefined) body.vector = query.vector;
  if (query.text !== undefined) body.text = query.text;
  if (query.filter !== undefined) body.filter = query.filter;
  return body;
}

function vectorMatchFromJson(data: JsonDict): VectorMatch {
  return {
    id: (data.id as string) ?? "",
    score: (data.score as number) ?? 0,
    payload: data.payload as JsonDict | undefined,
  };
}

function vectorRecordFromJson(data: JsonDict): VectorRecord {
  return {
    id: (data.id as string) ?? "",
    vector: data.vector as number[] | undefined,
    payload: data.payload as JsonDict | undefined,
  };
}

// -- The client -----------------------------------------------------------------------------------

export class InferHubClient {
  private readonly baseUrl: string;
  private readonly headers: Record<string, string>;
  private readonly timeoutMs?: number;
  private readonly fetchImpl: typeof fetch;

  constructor(options: InferHubClientOptions = {}) {
    this.baseUrl = (options.baseUrl ?? DEFAULT_BASE_URL).endsWith("/")
      ? (options.baseUrl ?? DEFAULT_BASE_URL)
      : `${options.baseUrl ?? DEFAULT_BASE_URL}/`;
    this.headers = buildHeaders(options.apiKey);
    this.timeoutMs = options.timeoutMs;
    this.fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
  }

  private url(path: string): string {
    return new URL(path, this.baseUrl).toString();
  }

  private async request(
    path: string,
    init: RequestInit & { headers?: Record<string, string> } = {},
  ): Promise<Response> {
    const controller = this.timeoutMs !== undefined ? new AbortController() : undefined;
    const timer =
      controller !== undefined ? setTimeout(() => controller.abort(), this.timeoutMs) : undefined;
    try {
      return await this.fetchImpl(this.url(path), {
        ...init,
        headers: { ...this.headers, ...(init.headers ?? {}) },
        signal: controller?.signal,
      });
    } finally {
      if (timer !== undefined) clearTimeout(timer);
    }
  }

  /** `GET /api/tags` — models advertised by the mesh. */
  async listModels(): Promise<TagsResponse> {
    const response = await this.request("api/tags", { method: "GET" });
    await raiseForStatus(response);
    return tagsResponseFromJson((await response.json()) as JsonDict);
  }

  /** Blocking chat — `POST /api/chat` with `stream:false`. A 424 throws
   * {@link InferHubRetrievalException}. `retrieval` builds the `X-InferHub-Retrieve*` headers for
   * this call only (D1) — it is never part of the request body. */
  async chat(request: ChatRequest, retrieval?: RetrievalOptions): Promise<ChatResponse> {
    const response = await this.request("api/chat", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...buildRetrievalHeaders(retrieval),
      },
      body: JSON.stringify(chatRequestToJson(request, false)),
    });
    await raiseForStatus(response);
    const result = chatResponseFromJson((await response.json()) as JsonDict);
    result.servedBy = readServedBy(response);
    result.sourceIds = readSourceIds(response);
    return result;
  }

  /** Streaming chat — `POST /api/chat` with `stream:true`. Yields one {@link ChatResponse} per
   * NDJSON line; a terminal error chunk throws {@link InferHubError} instead of the iterator
   * hanging or ending quietly. */
  async *chatStream(
    request: ChatRequest,
    retrieval?: RetrievalOptions,
  ): AsyncIterable<ChatResponse> {
    const response = await this.request("api/chat", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...buildRetrievalHeaders(retrieval),
      },
      body: JSON.stringify(chatRequestToJson(request, true)),
    });
    await raiseForStatus(response);
    if (!response.body) {
      throw new InferHubError(response.status, "InferHub streaming response had no body.");
    }
    const servedBy = readServedBy(response);
    const sourceIds = readSourceIds(response);
    for await (const line of readNdjsonLines(response.body)) {
      const chunk = parseNdjsonLine(line);
      if (chunk === undefined) continue;
      const result = chatResponseFromJson(chunk);
      result.servedBy = servedBy;
      result.sourceIds = sourceIds;
      yield result;
      if (result.done) return;
    }
  }

  /** Blocking generate — `POST /api/generate` with `stream:false`. */
  async generate(
    request: GenerateRequest,
    retrieval?: RetrievalOptions,
  ): Promise<GenerateResponse> {
    const response = await this.request("api/generate", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...buildRetrievalHeaders(retrieval),
      },
      body: JSON.stringify(generateRequestToJson(request, false)),
    });
    await raiseForStatus(response);
    const result = generateResponseFromJson((await response.json()) as JsonDict);
    result.servedBy = readServedBy(response);
    result.sourceIds = readSourceIds(response);
    return result;
  }

  /** Streaming generate — `POST /api/generate` with `stream:true`. */
  async *generateStream(
    request: GenerateRequest,
    retrieval?: RetrievalOptions,
  ): AsyncIterable<GenerateResponse> {
    const response = await this.request("api/generate", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...buildRetrievalHeaders(retrieval),
      },
      body: JSON.stringify(generateRequestToJson(request, true)),
    });
    await raiseForStatus(response);
    if (!response.body) {
      throw new InferHubError(response.status, "InferHub streaming response had no body.");
    }
    const servedBy = readServedBy(response);
    const sourceIds = readSourceIds(response);
    for await (const line of readNdjsonLines(response.body)) {
      const chunk = parseNdjsonLine(line);
      if (chunk === undefined) continue;
      const result = generateResponseFromJson(chunk);
      result.servedBy = servedBy;
      result.sourceIds = sourceIds;
      yield result;
      if (result.done) return;
    }
  }

  /** `POST /api/embed` — batch embeddings. An empty vector list on a 200 is treated as a
   * malformed response and thrown, never silently returned. */
  async embed(request: EmbedRequest): Promise<EmbedResponse> {
    const response = await this.request("api/embed", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ model: request.model, input: request.input }),
    });
    await raiseForStatus(response);
    const data = (await response.json()) as JsonDict;
    const embeddings = (data.embeddings as number[][] | undefined) ?? [];
    if (embeddings.length === 0) {
      throw new InferHubError(response.status, "embed response had no vectors");
    }
    return { model: (data.model as string) ?? "", embeddings };
  }

  /** `POST /api/embeddings` — the legacy single-input endpoint. Prefer {@link embed}. */
  async embedLegacy(request: EmbeddingsRequest): Promise<EmbeddingsResponse> {
    const response = await this.request("api/embeddings", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ model: request.model, prompt: request.prompt }),
    });
    await raiseForStatus(response);
    const data = (await response.json()) as JsonDict;
    const embedding = (data.embedding as number[] | undefined) ?? [];
    if (embedding.length === 0) {
      throw new InferHubError(response.status, "embeddings response had no vector");
    }
    return { embedding };
  }

  /** `GET /api/status` — coordinator/fleet snapshot. */
  async getStatus(): Promise<StatusResponse> {
    const response = await this.request("api/status", { method: "GET" });
    await raiseForStatus(response);
    return statusResponseFromJson((await response.json()) as JsonDict);
  }

  /** `GET /health` — `true` on 2xx, `false` otherwise. Never throws for a non-success status;
   * throws only on a transport error. */
  async ping(): Promise<boolean> {
    const response = await this.request("health", { method: "GET" });
    return response.ok;
  }

  // -- Vector data-plane (js/v0.2.0) -----------------------------------------------------------

  /** `POST /api/vector/{collection}/upsert`. */
  async upsert(collection: string, upsert: VectorUpsert): Promise<VectorRecord> {
    const response = await this.request(`api/vector/${collection}/upsert`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(vectorUpsertToJson(upsert)),
    });
    await raiseForStatus(response);
    return vectorRecordFromJson((await response.json()) as JsonDict);
  }

  /** `POST /api/vector/{collection}/query`. */
  async query(collection: string, query: VectorQuery): Promise<VectorMatch[]> {
    const response = await this.request(`api/vector/${collection}/query`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(vectorQueryToJson(query)),
    });
    await raiseForStatus(response);
    const data = (await response.json()) as JsonDict;
    return ((data.matches as JsonDict[] | undefined) ?? []).map(vectorMatchFromJson);
  }

  /** `POST /api/vector/{collection}/retrieve` — same shape as {@link query}, the RAG-oriented
   * route name the hub also answers on. */
  async retrieve(collection: string, query: VectorQuery): Promise<VectorMatch[]> {
    const response = await this.request(`api/vector/${collection}/retrieve`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(vectorQueryToJson(query)),
    });
    await raiseForStatus(response);
    const data = (await response.json()) as JsonDict;
    return ((data.matches as JsonDict[] | undefined) ?? []).map(vectorMatchFromJson);
  }

  /** `GET /api/vector/{collection}/{id}` — `undefined` on 404, never thrown (root rule 12). */
  async getRecord(collection: string, id: string): Promise<VectorRecord | undefined> {
    const response = await this.request(`api/vector/${collection}/${id}`, { method: "GET" });
    if (response.status === 404) return undefined;
    await raiseForStatus(response);
    return vectorRecordFromJson((await response.json()) as JsonDict);
  }

  /** `DELETE /api/vector/{collection}/{id}` — `true` iff a record was actually deleted. */
  async deleteRecord(collection: string, id: string): Promise<boolean> {
    const response = await this.request(`api/vector/${collection}/${id}`, { method: "DELETE" });
    if (response.status === 404) return false;
    await raiseForStatus(response);
    return response.ok;
  }

  // -- Ingestion and search (js/v0.2.0) --------------------------------------------------------
  // Thin wrappers over ./_corpus.ts (D3) — that module owns the wire shapes, this class only
  // supplies the same `request()` chat/generate/vectors already share.

  ingestText(collection: string, document: TextDocument): Promise<IngestResult> {
    return corpus.ingestText(this.request.bind(this), collection, document);
  }

  ingestFile(collection: string, document: FileDocument): Promise<IngestResult> {
    return corpus.ingestFile(this.request.bind(this), collection, document);
  }

  listDocuments(collection: string): Promise<DocumentSummary[]> {
    return corpus.listDocuments(this.request.bind(this), collection);
  }

  getDocument(collection: string, documentId: string): Promise<DocumentSummary | undefined> {
    return corpus.getDocument(this.request.bind(this), collection, documentId);
  }

  getChunks(collection: string, documentId: string): Promise<DocumentChunksResponse> {
    return corpus.getChunks(this.request.bind(this), collection, documentId);
  }

  deleteDocument(collection: string, documentId: string): Promise<DocumentDeletion | undefined> {
    return corpus.deleteDocument(this.request.bind(this), collection, documentId);
  }

  /** `search(collection, "a question")` or `search(collection, { query: "...", topK: 5 })`. */
  search(collection: string, query: string | SearchRequest): Promise<SearchResponse> {
    return corpus.search(this.request.bind(this), collection, query);
  }
}
