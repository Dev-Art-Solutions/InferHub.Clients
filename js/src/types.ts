/**
 * Typed request/response shapes for the Ollama-dialect core surface (chat, generate, embeddings,
 * model listing, status, health) — v0.1.0's scope only; retrieval and modalities land in
 * `0.2.0`/`1.0.0` (see `plans/phase-19-js-core.md`).
 *
 * Plain interfaces, not classes: the wire is small and stable, and a validation library would be
 * a dependency every consumer of this package inherits (root `CLAUDE.md` rule 2). Every response
 * type carries an `extra` record for fields the hub sends that this version does not know about
 * yet (D4) — the same escape hatch python 16's `extra` dict and the C# client's
 * `[JsonExtensionData]` give theirs.
 */

export type JsonValue =
  | string
  | number
  | boolean
  | null
  | JsonValue[]
  | { [key: string]: JsonValue };

export type JsonDict = Record<string, unknown>;

export interface ChatMessage {
  role: string;
  content?: string;
  images?: string[];
  toolCalls?: JsonDict[];
  /** Fields this version does not know about — merged in on the way out, kept apart on the way in. */
  extra?: JsonDict;
}

export interface ChatRequest {
  model: string;
  messages: ChatMessage[];
  /** Set by `chat()`/`chatStream()` — not meant to be set by the caller directly. */
  stream?: boolean;
  options?: JsonDict;
  format?: string | JsonDict;
  keepAlive?: string;
  /** Merges straight into the top-level request body — every Ollama option this client does not
   * type (`options`, `format`, `keep_alive`, tool definitions) stays reachable without a new
   * release (non-goal in the phase-19 brief, same as C# and Python). */
  extra?: JsonDict;
}

export interface ChatResponse {
  model: string;
  createdAt?: string;
  message?: ChatMessage;
  done?: boolean;
  doneReason?: string;
  totalDuration?: number;
  loadDuration?: number;
  promptEvalCount?: number;
  promptEvalDuration?: number;
  evalCount?: number;
  evalDuration?: number;
  error?: string;
  extra: JsonDict;
  /** Set from response headers, never the body — root `CLAUDE.md` rule 8: surfaced, never
   * interpreted. This client does not route or retry elsewhere on it. */
  servedBy?: string;
  /** `X-InferHub-Sources`, parsed read-only (D3 non-goal note) — reachable even though v0.1.0 has
   * no way yet to opt into retrieval. */
  sourceIds?: string[];
}

export interface GenerateRequest {
  model: string;
  prompt?: string;
  stream?: boolean;
  options?: JsonDict;
  format?: string | JsonDict;
  keepAlive?: string;
  extra?: JsonDict;
}

export interface GenerateResponse {
  model: string;
  createdAt?: string;
  response: string;
  done?: boolean;
  doneReason?: string;
  context?: number[];
  totalDuration?: number;
  loadDuration?: number;
  promptEvalCount?: number;
  promptEvalDuration?: number;
  evalCount?: number;
  evalDuration?: number;
  error?: string;
  extra: JsonDict;
  servedBy?: string;
  sourceIds?: string[];
}

export interface EmbedRequest {
  model: string;
  input: string | string[];
}

export interface EmbedResponse {
  model: string;
  embeddings: number[][];
}

export interface EmbeddingsRequest {
  model: string;
  prompt: string;
}

export interface EmbeddingsResponse {
  embedding: number[];
}

export interface ModelInfo {
  name: string;
  digest?: string;
  size?: number;
}

export interface TagsResponse {
  models: ModelInfo[];
}

export interface StatusResponse {
  coordinatorVersion?: string;
  nowUtc?: string;
  uptimeSeconds?: number;
  nodes?: JsonDict[];
  models?: ModelInfo[];
  extra: JsonDict;
}

/** Options passed to the {@link InferHubClient} constructor. */
export interface InferHubClientOptions {
  baseUrl?: string;
  apiKey?: string;
  /** Milliseconds. `undefined` means no client-side timeout. */
  timeoutMs?: number;
  /** Override for tests, or to point at a runtime's own `fetch` (all four target runtimes have
   * one natively — this is an escape hatch, not a required wiring step). */
  fetch?: typeof fetch;
}
