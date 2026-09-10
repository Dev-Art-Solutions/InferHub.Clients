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

// -- Audio (v1.0.0) --------------------------------------------------------------------------
// POST /v1/audio/transcriptions, POST /v1/audio/speech. No InferHubCallOptions-equivalent here,
// deliberately (dotnet phase-9 D7): neither route reads X-InferHub-Provider,
// X-InferHub-Conversation or the retrieval headers — audio dispatches to a node that declared the
// capability, no cloud provider is in the path.

/** `POST /v1/audio/transcriptions`, multipart. `audio` is handed to `FormData` as-is — this
 * client never copies file content into memory or holds it past the request. */
export interface TranscriptionRequest {
  model: string;
  audio: Blob;
  filename: string;
  contentType?: string;
  language?: string;
  prompt?: string;
  temperature?: number;
  responseFormat?: string;
}

export interface TranscriptionSegment {
  id?: number;
  start?: number;
  end?: number;
  text: string;
  extra: JsonDict;
}

/** `transcribe()`'s answer — always requested as `verbose_json` regardless of what the caller
 * asked for (dotnet D6), because these are the fields a caller does something with. For
 * `text`/`srt`/`vtt` use `transcribeDocument`. */
export interface Transcription {
  text: string;
  language?: string;
  duration?: number;
  segments: TranscriptionSegment[];
  extra: JsonDict;
}

/** The hub's `text`/`srt`/`vtt` output, returned unaltered — the only honest thing to do with a
 * subtitle file. */
export interface TranscriptionDocument {
  content: string;
  contentType: string;
  servedBy?: string;
}

/** `POST /v1/audio/speech`. `streamFormat` is `undefined` for the whole file at once, `"audio"`
 * for a framed binary stream, or `"sse"` (forced by `streamSpeech`, never set here directly) —
 * only `wav`/`pcm` can stream; anything else is a `400` from the hub before a node is chosen. */
export interface SpeechRequest {
  model: string;
  input: string;
  voice?: string;
  responseFormat?: string;
  streamFormat?: string;
}

/** `createSpeech()`'s answer — the live `Response`, read-once by nature of being an HTTP stream
 * (root rule 7): the caller consumes `.body`/`.arrayBuffer()` and this client never buffers it.
 * `sampleRate`/`characters` are `undefined` unless the hub measured and stamped one (streaming
 * synthesis only), never a guessed value. */
export interface SpeechAudio {
  response: Response;
  contentType?: string;
  sampleRate?: number;
  characters?: number;
  servedBy?: string;
}

/** One SSE frame of `streamSpeech()` — a `speech.audio.delta` carrying `audio`, or the terminal
 * `speech.audio.done` carrying `usage` and no audio. `audio` is decoded from the frame's base64.
 * The terminal frame is yielded like any other rather than swallowed: a usage of three zeros is a
 * true count for a phoneme model that tokenized nothing, and the number that reconciles with a
 * bill is `characters` — read once from a response header and stamped on every chunk, not a body
 * field. */
export interface SpeechChunk {
  type: string;
  audio?: Uint8Array;
  usage?: JsonDict;
  characters?: number;
  servedBy?: string;
  sampleRate?: number;
  extra: JsonDict;
}

// -- Images (v1.0.0) -------------------------------------------------------------------------
// The synchronous /v1/images/* routes and the async job seam, /api/images/jobs. Same OpenAI
// envelope and same InferHubOpenAiException on both.

/** The `X-InferHub-Image-*` extension headers — not body fields on the hub. */
export interface ImageOptions {
  steps?: number;
  guidance?: number;
  seed?: number;
  strength?: number;
  maskConvention?: string;
  seamRepair?: string;
  projection?: string;
}

export interface ImageGenerationRequest {
  model: string;
  prompt: string;
  negativePrompt?: string;
  n?: number;
  size?: string;
  seed?: number;
  responseFormat?: string;
  options?: ImageOptions;
}

/** A picture and a prompt, multipart. With a mask, only the masked area is redrawn; without one,
 * this is image-to-image. Two request types, not one with an `operation` field (dotnet D5): the
 * hub's two refusals — "a variation takes no prompt", "a variation takes no mask" — are then
 * unrepresentable in this client's types instead of merely disallowed. */
export interface ImageEditRequest {
  model: string;
  image: Blob;
  imageFilename: string;
  prompt: string;
  mask?: Blob;
  maskFilename?: string;
  imageContentType?: string;
  maskContentType?: string;
  options?: ImageOptions;
}

/** No prompt, no mask: see `ImageEditRequest`'s remarks. */
export interface ImageVariationRequest {
  model: string;
  image: Blob;
  imageFilename: string;
  imageContentType?: string;
  options?: ImageOptions;
}

export interface ImageData {
  b64Json?: string;
  size?: string;
  seed?: number;
  projection?: string;
  seamDelta?: number;
  seamRepair?: string;
  seamDeltaBefore?: number;
  revisedPrompt?: string;
  extra: JsonDict;
}

/** `POST /v1/images/generations|edits|variations`'s answer — pictures come back base64 in the
 * envelope, because the hub stores nothing and so has no URL to serve. For a render that should
 * outlive one HTTP connection, submit it as a job instead. */
export interface ImageResponse {
  created?: number;
  data: ImageData[];
  promptAugmented?: string;
  trigger?: string;
  warnings?: string[];
  extra: JsonDict;
}

export interface MediaJobOutput {
  index?: number;
  url?: string;
  extra: JsonDict;
}

/** The one job document both images and video jobs render through (dotnet D2 — the hub's
 * `ImageJobView.Describe` serves both capabilities from one type, `capability` telling them
 * apart), reused here rather than typed twice. */
export interface MediaJob {
  id: string;
  state: string;
  capability: string;
  step?: number;
  totalSteps?: number;
  images: MediaJobOutput[];
  extra: JsonDict;
}

/** `GET /api/images/jobs` — client-scoped, never fleet-wide: holding a job id is how a picture is
 * fetched, so listing other tenants' ids would be handing them out. Lists work, not results — a
 * job whose images were already delivered is still here with nothing left to fetch. */
export interface MediaJobList {
  jobs: MediaJob[];
  queued: number;
  active: number;
  retainedBytes: number;
  retentionSeconds: number;
  persistence: string;
}

/** `GET /api/images/jobs/{id}/content/{index}` — read once: the hub unlinks the bytes as they are
 * read, so a retry is a `410`. The caller consumes `response.body`/`.arrayBuffer()`; this client
 * never buffers it. */
export interface ImageContent {
  response: Response;
  contentType?: string;
  projection?: string;
  seamRepair?: string;
}

// -- Admin (v1.0.0) --------------------------------------------------------------------------
// /api/admin/*, an admin key, and the vector-collection lifecycle. Plain {"error": "..."}
// envelope (never the OpenAI one), so admin failures surface as InferHubError.
//
// Selectors/model catalogues/retrieval-profile bodies stay as passthrough JsonDict rather than a
// fully typed hierarchy: this client does not validate them client-side, so nothing is lost by
// not naming every nested field, and the alternative is a dozen near-empty interfaces that exist
// only to be re-serialized unchanged.

export interface AdminNode {
  nodeId: string;
  name?: string;
  extra: JsonDict;
}

export interface CollectionInfo {
  name: string;
  dimension: number;
  distance?: string;
  recordCount?: number;
  operations?: number;
  extra: JsonDict;
}

export interface CollectionsResponse {
  collections: CollectionInfo[];
  extra: JsonDict;
}

export interface CollectionDetail {
  name: string;
  dimension: number;
  distance?: string;
  underReplicated?: boolean;
  extra: JsonDict;
}

/** One shape for both directions (dotnet D6) — `name`/`revision` are ignored on write, the hub
 * sets both from the route and its own counter regardless of what is sent. */
export interface NodeProfile {
  name: string;
  revision: number;
  selector: JsonDict;
  models?: JsonDict;
  maxConcurrency?: number;
  retrieval?: JsonDict;
  extra: JsonDict;
}

export interface PutProfileResult {
  profile?: NodeProfile;
  applied: string[];
  conflicts: string[];
}

export interface DeleteProfileResult {
  reasserted: string[];
  extra: JsonDict;
}

export interface NodeProfileState {
  nodeId: string;
  desired?: JsonDict;
  effective?: JsonDict;
  refusals: JsonDict[];
  extra: JsonDict;
}

/** The literal `202` body a pull/delete/warm command answers with. `reused` means somebody
 * already asked — surfaced, not hidden: a caller polling for their own command id needs to know
 * it may be watching someone else's. */
export interface ModelCommandAccepted {
  nodeId: string;
  model: string;
  kind: string;
  commandId: string;
  reused: boolean;
  extra: JsonDict;
}

/** `GET /api/admin/models` — kept as a thin wrapper over the wire shape rather than a typed grid:
 * which nodes hold each model is exactly the shape the hub sends and a caller reads it the same
 * way it arrived. */
export interface FleetModelMatrix {
  models: JsonDict[];
  nodes: JsonDict[];
  extra: JsonDict;
}

/** `POST /api/admin/models/{model}/ensure` — the hub's full placement reasoning, not just a
 * boolean (dotnet D3): `decision` is what an operator escalates on. */
export interface EnsureModelResult {
  satisfied: boolean;
  decision: JsonDict;
  extra: JsonDict;
}

/** The hub's actual `GET /api/admin/usage` projection (dotnet D4) — counts only, never a prompt
 * or a completion (hub rule 7). */
export interface UsageRow {
  clientId: string;
  model: string;
  requests: number;
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  fallbackRequests: number;
}

export interface UsageResponse {
  rows: UsageRow[];
}

/** Never a key, by construction (dotnet D5) — `ClientConfig.Key` never leaves the hub process, so
 * there is no field here for a caller to notice is always empty. */
export interface ClientRow {
  clientId: string;
  limits?: JsonDict;
  liveUsage?: JsonDict;
  extra: JsonDict;
}

/** One frame of `GET /api/admin/stream` — `event` is the SSE event name (`snapshot`,
 * `vector.*`, `model-progress`, ...), `data` its parsed JSON payload. No reconnect variant is
 * offered: this client's contract is one HTTP connection, one generator, ending when the server
 * closes the stream or the caller stops iterating. */
export interface AdminEvent {
  event?: string;
  data: JsonDict;
}

// -- The node (v1.0.0) -----------------------------------------------------------------------
// A base address, not a second client (root rule 6 / roadmap D7).

export interface NodeBackendInfo {
  name?: string;
  endpoint?: string;
  health?: string;
}

export interface NodeConcurrency {
  limit: number;
  inFlight: number;
}

export interface NodeGpuInfo {
  cuda: boolean;
  devices: number;
  names: string[];
}

/** `rerank` is a **string** (`"none"`/`"llm"`, the config-level rerank mode), never a boolean —
 * the conformance corpus's founding case (`node-status-rerank-is-a-string`): dotnet typed it
 * `bool?` in `v1.7.0` and threw the first time it was driven against a real node with retrieval
 * on. This client is typed correctly from the start because the case exists before the bug had a
 * chance to happen here. */
export interface NodeRetrievalInfo {
  enabled: boolean;
  provider?: string;
  embeddingModel?: string;
  mode?: string;
  rerank?: string;
  collections: JsonDict[];
  error?: string;
}

/** A solo node's `GET /api/status` — deliberately a smaller, different document than
 * `StatusResponse`. `mode` is always `"solo"` and is the only field that tells the two documents
 * apart; there is no fleet array, no queue block, no replica count, because a node with no
 * coordinator has no concept of any of them. */
export interface NodeStatusResponse {
  mode: string;
  nodeVersion?: string;
  nowUtc?: string;
  name?: string;
  backend?: NodeBackendInfo;
  concurrency?: NodeConcurrency;
  gpu?: NodeGpuInfo;
  capabilities: string[];
  retrieval?: NodeRetrievalInfo;
  models: ModelInfo[];
  extra: JsonDict;
}

/** `"hub"` | `"solo_node"` — `InferHubTargetProbe.kind`. */
export type InferHubTargetKind = "hub" | "solo_node";

/** `probe()`'s answer — one `GET /api/status`, discriminated on whether the body carries `mode`
 * (present → a solo node; the hub's document never has the field at all). Exactly one of
 * `hubStatus`/`nodeStatus` is set, matching `kind`. */
export interface InferHubTargetProbe {
  kind: InferHubTargetKind;
  version?: string;
  hubStatus?: StatusResponse;
  nodeStatus?: NodeStatusResponse;
}

/** The hub's `501 not_supported` refusals on `GET /v1/videos` and `POST /v1/videos/{id}/remix`
 * (root rule 10) — taught here rather than published as methods that can only throw. There is no
 * video module in this client: an id is itself the capability to fetch the bytes, and nothing
 * durable holds the prompt that made a clip, so neither a listing nor a remix can ever be served.
 * See `js/README.md`'s Video section for the recorded refusal bodies and the alternative (send a
 * new request with the prompt you want). */
export const VideoErrorCodes = {
  NOT_SUPPORTED: "not_supported",
} as const;
