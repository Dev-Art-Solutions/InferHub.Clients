/**
 * `InferHubClient` — one class, `fetch`-based, no sync/async split (D1). `chat`/`generate` return
 * `Promise<T>`; `chatStream`/`generateStream` return `AsyncIterable<T>`. Covers the Ollama-dialect
 * core surface only: chat, generate (blocking + streaming), embeddings, model listing,
 * status/health. Retrieval and modalities/admin/node are later phases (`js/v0.2.0`, `js/v1.0.0`).
 */

import { buildHeaders, DEFAULT_BASE_URL, parseNdjsonLine, raiseForStatus, readServedBy, readSourceIds } from "./_base.js";
import { readNdjsonLines } from "./_stream.js";
import { InferHubError } from "./errors.js";
import type {
  ChatMessage,
  ChatRequest,
  ChatResponse,
  EmbeddingsRequest,
  EmbeddingsResponse,
  EmbedRequest,
  EmbedResponse,
  GenerateRequest,
  GenerateResponse,
  InferHubClientOptions,
  JsonDict,
  ModelInfo,
  StatusResponse,
  TagsResponse,
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
   * {@link InferHubRetrievalException}. */
  async chat(request: ChatRequest): Promise<ChatResponse> {
    const response = await this.request("api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
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
  async *chatStream(request: ChatRequest): AsyncIterable<ChatResponse> {
    const response = await this.request("api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
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
  async generate(request: GenerateRequest): Promise<GenerateResponse> {
    const response = await this.request("api/generate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(generateRequestToJson(request, false)),
    });
    await raiseForStatus(response);
    const result = generateResponseFromJson((await response.json()) as JsonDict);
    result.servedBy = readServedBy(response);
    result.sourceIds = readSourceIds(response);
    return result;
  }

  /** Streaming generate — `POST /api/generate` with `stream:true`. */
  async *generateStream(request: GenerateRequest): AsyncIterable<GenerateResponse> {
    const response = await this.request("api/generate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
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
}
