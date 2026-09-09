/**
 * Ingestion and search — `/api/collections/{collection}/**`. A separate module (D3, mirroring
 * python 17's `_corpus.py`) rather than folded into `client.ts`: the corpus surface is seven
 * methods with multipart upload and its own 500-with-a-body shape, and putting it beside
 * chat/generate/vectors would leave `client.ts` covering three unrelated planes. Each function
 * takes the client's own `request()` so there is nothing here that touches `fetch` directly.
 */

import { raiseForStatus } from "./_base.js";
import type {
  DocumentChunksResponse,
  DocumentDeletion,
  DocumentSummary,
  FileDocument,
  IngestResult,
  JsonDict,
  SearchRequest,
  SearchResponse,
  TextDocument,
} from "./types.js";

export type Requester = (
  path: string,
  init?: RequestInit & { headers?: Record<string, string> },
) => Promise<Response>;

function textDocumentToJson(document: TextDocument): JsonDict {
  const body: JsonDict = { id: document.id, text: document.text };
  if (document.metadata !== undefined) body.metadata = document.metadata;
  return body;
}

function searchRequestToJson(request: SearchRequest): JsonDict {
  const body: JsonDict = { query: request.query, topK: request.topK ?? 10 };
  if (request.mode !== undefined) body.mode = request.mode;
  if (request.rerank !== undefined) body.rerank = request.rerank;
  if (request.filter !== undefined) body.filter = request.filter;
  return body;
}

const _KNOWN_INGEST_RESULT_FIELDS = new Set([
  "documentId",
  "collection",
  "status",
  "chunks",
  "chunksEmbedded",
  "bytes",
  "contentHash",
  "error",
]);

function ingestResultFromJson(data: JsonDict): IngestResult {
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) {
    if (!_KNOWN_INGEST_RESULT_FIELDS.has(key)) extra[key] = value;
  }
  return {
    documentId: (data.documentId as string) ?? "",
    collection: (data.collection as string) ?? "",
    status: (data.status as string) ?? "",
    chunks: (data.chunks as number) ?? 0,
    chunksEmbedded: (data.chunksEmbedded as number) ?? 0,
    bytes: (data.bytes as number) ?? 0,
    contentHash: data.contentHash as string | undefined,
    error: data.error as string | undefined,
    extra,
  };
}

/** A body has this shape iff it carries both `documentId` and `status` — used to tell an
 * {@link IngestResult} (even on a 500) apart from a genuine error envelope (`{"error": ...}` with
 * neither field). */
function looksLikeIngestResult(data: unknown): data is JsonDict {
  return (
    data !== null &&
    typeof data === "object" &&
    "documentId" in (data as JsonDict) &&
    "status" in (data as JsonDict)
  );
}

/** A `partial` ingest is an HTTP 500 **with an `IngestResult` body**, returned rather than raised
 * (root rule 11, conformance case `partial-ingest-is-a-500-with-a-body-not-thrown`). Anything else
 * non-2xx — a genuine error envelope, or a body with neither `documentId` nor `status` — still
 * throws normally. */
async function ingestResultOrThrow(response: Response): Promise<IngestResult> {
  if (!response.ok) {
    const clone = response.clone();
    let data: unknown;
    try {
      data = await clone.json();
    } catch {
      data = undefined;
    }
    if (looksLikeIngestResult(data)) {
      return ingestResultFromJson(data);
    }
    await raiseForStatus(response);
  }
  return ingestResultFromJson((await response.json()) as JsonDict);
}

function documentSummaryFromJson(data: JsonDict): DocumentSummary {
  const known = new Set(["documentId", "collection", "status", "chunks", "bytes"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) {
    if (!known.has(key)) extra[key] = value;
  }
  return {
    documentId: (data.documentId as string) ?? "",
    collection: (data.collection as string) ?? "",
    status: (data.status as string) ?? "",
    chunks: (data.chunks as number) ?? 0,
    bytes: (data.bytes as number) ?? 0,
    extra,
  };
}

function documentChunksResponseFromJson(data: JsonDict): DocumentChunksResponse {
  const chunks = (data.chunks as JsonDict[] | undefined) ?? [];
  return {
    collection: (data.collection as string) ?? "",
    documentId: (data.documentId as string) ?? "",
    chunks: chunks.map((c) => ({
      id: (c.id as string) ?? "",
      index: (c.index as string) ?? "",
      page: c.page as number | undefined,
      text: (c.text as string) ?? "",
    })),
  };
}

function documentDeletionFromJson(data: JsonDict): DocumentDeletion {
  return { documentId: (data.documentId as string) ?? "", deleted: Boolean(data.deleted) };
}

function searchResponseFromJson(data: JsonDict): SearchResponse {
  const hits = (data.hits as JsonDict[] | undefined) ?? [];
  return {
    collection: (data.collection as string) ?? "",
    mode: (data.mode as string) ?? "",
    hits: hits.map((h) => ({
      id: (h.id as string) ?? "",
      score: (h.score as number) ?? 0,
      documentId: (h.documentId as string) ?? "",
      text: (h.text as string) ?? "",
    })),
  };
}

/** `POST /api/collections/{collection}/documents` with a JSON body. */
export async function ingestText(
  request: Requester,
  collection: string,
  document: TextDocument,
): Promise<IngestResult> {
  const response = await request(`api/collections/${collection}/documents`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(textDocumentToJson(document)),
  });
  return ingestResultOrThrow(response);
}

/** `POST /api/collections/{collection}/documents` as multipart, via `FormData` — the platform's
 * own multipart encoder, no dependency (D5). The file field is appended last: some multipart
 * parsers are order-sensitive, matching the C# client's `MultipartFormDataContent` ordering. */
export async function ingestFile(
  request: Requester,
  collection: string,
  document: FileDocument,
): Promise<IngestResult> {
  const form = new FormData();
  form.append("id", document.id);
  if (document.metadata !== undefined) {
    form.append("metadata", JSON.stringify(document.metadata));
  }
  const blob =
    document.contentType && document.contentType !== document.body.type
      ? document.body.slice(0, document.body.size, document.contentType)
      : document.body;
  form.append("file", blob, document.filename);
  const response = await request(`api/collections/${collection}/documents`, {
    method: "POST",
    body: form,
  });
  return ingestResultOrThrow(response);
}

export async function listDocuments(
  request: Requester,
  collection: string,
): Promise<DocumentSummary[]> {
  const response = await request(`api/collections/${collection}/documents`, { method: "GET" });
  await raiseForStatus(response);
  const data = (await response.json()) as JsonDict;
  const documents = (data.documents as JsonDict[] | undefined) ?? [];
  return documents.map(documentSummaryFromJson);
}

/** `GET /api/collections/{collection}/documents/{documentId}` — `undefined` on 404, never thrown
 * (root rule 12: a 404 that names one thing is an absence). */
export async function getDocument(
  request: Requester,
  collection: string,
  documentId: string,
): Promise<DocumentSummary | undefined> {
  const response = await request(`api/collections/${collection}/documents/${documentId}`, {
    method: "GET",
  });
  if (response.status === 404) return undefined;
  await raiseForStatus(response);
  return documentSummaryFromJson((await response.json()) as JsonDict);
}

export async function getChunks(
  request: Requester,
  collection: string,
  documentId: string,
): Promise<DocumentChunksResponse> {
  const response = await request(
    `api/collections/${collection}/documents/${documentId}/chunks`,
    { method: "GET" },
  );
  await raiseForStatus(response);
  return documentChunksResponseFromJson((await response.json()) as JsonDict);
}

/** `DELETE /api/collections/{collection}/documents/{documentId}` — `undefined` on 404. */
export async function deleteDocument(
  request: Requester,
  collection: string,
  documentId: string,
): Promise<DocumentDeletion | undefined> {
  const response = await request(`api/collections/${collection}/documents/${documentId}`, {
    method: "DELETE",
  });
  if (response.status === 404) return undefined;
  await raiseForStatus(response);
  return documentDeletionFromJson((await response.json()) as JsonDict);
}

/** `POST /api/collections/{collection}/search`. Unlike `getDocument`/`getChunks`/`deleteDocument`,
 * this **throws** on a collection that does not exist (root rule 12): answering "no hits" for a
 * misspelled collection reports an empty corpus as a working one. */
export async function search(
  request: Requester,
  collection: string,
  query: string | SearchRequest,
): Promise<SearchResponse> {
  const searchRequest: SearchRequest = typeof query === "string" ? { query } : query;
  const response = await request(`api/collections/${collection}/search`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(searchRequestToJson(searchRequest)),
  });
  await raiseForStatus(response);
  return searchResponseFromJson((await response.json()) as JsonDict);
}
