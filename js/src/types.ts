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

// -- Retrieval (v0.2.0) ---------------------------------------------------------------------------
// Vector data-plane, RAG headers, ingestion and search — ports python 17's `_models.py` slice
// (see plans/phase-20-js-retrieval.md D1-D3).

export type RetrievalMode = string;

/** Carried on a `chat`/`chatStream`/`generate`/`generateStream` call, never on the request object
 * itself (D1): builds the `X-InferHub-Retrieve*`/`X-InferHub-Rerank` headers for that call only. */
export interface RetrievalOptions {
  collection: string;
  k?: number;
  model?: string;
  mode?: RetrievalMode;
  rerank?: boolean;
}

/** `POST /api/vector/{collection}/upsert`. Exactly one of `vector`/`text` is set — the hub embeds
 * `text` itself when a vector is not supplied. */
export interface VectorUpsert {
  id: string;
  vector?: number[];
  text?: string;
  payload?: JsonDict;
}

/** `POST /api/vector/{collection}/query` (or `/retrieve` — same shape). Exactly one of
 * `vector`/`text` is set, same rule as {@link VectorUpsert}. */
export interface VectorQuery {
  vector?: number[];
  text?: string;
  topK?: number;
  filter?: JsonDict;
}

export interface VectorMatch {
  id: string;
  score: number;
  payload?: JsonDict;
}

export interface VectorRecord {
  id: string;
  vector?: number[];
  payload?: JsonDict;
}

/** `POST /api/collections/{collection}/documents` with a JSON body — a document supplied as text. */
export interface TextDocument {
  id: string;
  text: string;
  metadata?: JsonDict;
}

/** A document supplied as a file. `body` is handed to `fetch`'s `FormData` as-is (a `Blob`, a
 * `File`, or anything `FormData.append` accepts) — this client never copies file content into
 * memory or holds it past the request (root rule 4, extended to corpus content). */
export interface FileDocument {
  id: string;
  filename: string;
  body: Blob;
  contentType?: string;
  metadata?: JsonDict;
}

/** The hub's own answer to an ingest call — `ingested`, `unchanged` or `partial`. `partial`
 * arrives as an HTTP 500 **with this exact body**, and {@link InferHubClient.ingestText}/
 * {@link InferHubClient.ingestFile} return it rather than throwing (root rule 11). */
export interface IngestResult {
  documentId: string;
  collection: string;
  status: string;
  chunks: number;
  chunksEmbedded: number;
  bytes: number;
  contentHash?: string;
  error?: string;
  extra: JsonDict;
}

export interface DocumentSummary {
  documentId: string;
  collection: string;
  status: string;
  chunks: number;
  bytes: number;
  extra: JsonDict;
}

/** `index` is a **string**, not a number — the hub's chunk metadata is a string map; `page`, when
 * present, is a real number on the same response (the asymmetry conformance case
 * `chunk-index-is-a-string-not-an-int` exists to catch). */
export interface DocumentChunk {
  id: string;
  index: string;
  page?: number;
  text: string;
}

export interface DocumentChunksResponse {
  collection: string;
  documentId: string;
  chunks: DocumentChunk[];
}

export interface DocumentDeletion {
  documentId: string;
  deleted: boolean;
}

/** `POST /api/collections/{collection}/search`. `mode`/`rerank` are body fields here — unlike
 * chat/generate, search takes them in the request rather than as headers. */
export interface SearchRequest {
  query: string;
  topK?: number;
  mode?: RetrievalMode;
  rerank?: boolean;
  filter?: JsonDict;
}

export interface SearchHit {
  id: string;
  score: number;
  documentId: string;
  text: string;
}

/** `hits` is kept in the hub's own wire order, never re-sorted by score (root rule 11 — a
 * reranked result routinely has a lower score above a higher one). */
export interface SearchResponse {
  collection: string;
  mode: string;
  hits: SearchHit[];
}
