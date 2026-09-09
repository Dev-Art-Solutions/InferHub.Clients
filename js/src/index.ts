/** Public exports only (D5's runner reads `client.ts`/`_base.ts` directly; nothing prefixed `_`
 * is exported here). */

export { InferHubClient } from "./client.js";
export { InferHubError, InferHubOpenAiException, InferHubRetrievalException } from "./errors.js";
export type {
  ChatMessage,
  ChatRequest,
  ChatResponse,
  DocumentChunk,
  DocumentChunksResponse,
  DocumentDeletion,
  DocumentSummary,
  EmbedRequest,
  EmbedResponse,
  EmbeddingsRequest,
  EmbeddingsResponse,
  FileDocument,
  GenerateRequest,
  GenerateResponse,
  IngestResult,
  InferHubClientOptions,
  JsonDict,
  JsonValue,
  ModelInfo,
  RetrievalMode,
  RetrievalOptions,
  SearchHit,
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
