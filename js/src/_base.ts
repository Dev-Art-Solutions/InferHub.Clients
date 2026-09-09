/**
 * Shared, I/O-free plumbing used by `InferHubClient`: header building, error mapping, and NDJSON
 * chunk parsing — kept apart from `client.ts` so each method stays a thin call rather than
 * repeating this logic (ports python `_base.py`'s behavior; see it for the corpus cases this
 * mirrors).
 */

import {
  InferHubError,
  InferHubOpenAiException,
  InferHubRetrievalException,
} from "./errors.js";
import type { JsonDict, RetrievalOptions } from "./types.js";

export const DEFAULT_BASE_URL = "http://localhost:5080/";

/**
 * No default `Content-Type` here (phase-17 finding, ported unchanged): a client-level default
 * header would win over what a per-request `Content-Type` should be, which silently breaks a
 * future multipart call. `fetch` already sets the right content type for a `JSON.stringify`d
 * body when the caller sets one explicitly per request, which every method here does.
 */
export function buildHeaders(apiKey?: string): Record<string, string> {
  const headers: Record<string, string> = {};
  if (apiKey) {
    headers.Authorization = `Bearer ${apiKey}`;
  }
  return headers;
}

/** The five `X-InferHub-Retrieve*`/`X-InferHub-Rerank` headers for one `chat`/`generate` call. A
 * call-scoped concern kept off the request object on purpose (D1) — retrieval applies to both
 * `chat` and `generate`, and folding it into either request's serializer would mean the body
 * builder has to know to exclude a header-only field. */
export function buildRetrievalHeaders(options?: RetrievalOptions): Record<string, string> {
  if (!options) {
    return {};
  }
  const headers: Record<string, string> = { "X-InferHub-Retrieve": options.collection };
  if (options.k !== undefined) headers["X-InferHub-Retrieve-K"] = String(options.k);
  if (options.model !== undefined) headers["X-InferHub-Retrieve-Model"] = options.model;
  if (options.mode !== undefined) headers["X-InferHub-Retrieve-Mode"] = options.mode;
  if (options.rerank !== undefined) headers["X-InferHub-Rerank"] = String(options.rerank);
  return headers;
}

/** The Ollama dialect answers `{"error": "..."}`. A non-JSON or differently-shaped body falls
 * back to the raw text, same as the C# client's `TryExtractErrorMessage`. */
function extractErrorMessage(body: string): string | undefined {
  if (!body.trim()) {
    return undefined;
  }
  try {
    const parsed: unknown = JSON.parse(body);
    if (
      parsed !== null &&
      typeof parsed === "object" &&
      "error" in parsed &&
      typeof (parsed as { error: unknown }).error === "string"
    ) {
      return (parsed as { error: string }).error;
    }
  } catch {
    return body;
  }
  return body;
}

interface OpenAiEnvelope {
  message: string;
  code?: string;
  param?: string;
  errorType?: string;
}

/** `{"error":{"message":...,"type":...,"param":...,"code":...}}` — the OpenAI dialect's envelope,
 * used by `/v1/*` and by routes that reuse its error shape. Returns `undefined` when the body is
 * not this shape, so the caller falls back to the Ollama dialect's plain-string envelope. */
function parseOpenAiEnvelope(body: string): OpenAiEnvelope | undefined {
  let parsed: unknown;
  try {
    parsed = JSON.parse(body);
  } catch {
    return undefined;
  }
  if (parsed === null || typeof parsed !== "object") {
    return undefined;
  }
  const error = (parsed as { error?: unknown }).error;
  if (error === null || typeof error !== "object") {
    return undefined;
  }
  const errorObj = error as Record<string, unknown>;
  const message = typeof errorObj.message === "string" ? errorObj.message : body;
  return {
    message,
    code: typeof errorObj.code === "string" ? errorObj.code : undefined,
    param: typeof errorObj.param === "string" ? errorObj.param : undefined,
    errorType: typeof errorObj.type === "string" ? errorObj.type : undefined,
  };
}

function retryAfter(response: Response): number | undefined {
  const header = response.headers.get("Retry-After");
  if (!header) {
    return undefined;
  }
  const value = Number(header);
  return Number.isNaN(value) ? undefined : value; // An HTTP-date form exists but the hub always
  // writes delta-seconds, same carve-out python's _retry_after takes.
}

/**
 * Which envelope arrived decides the exception type, never which method was called (root
 * `CLAUDE.md` rule 9): a `{"error":{...}}` object throws {@link InferHubOpenAiException} with
 * `errorCode`/`param` intact, a `{"error":"..."}` string throws the base {@link InferHubError}.
 * A 424 is always {@link InferHubRetrievalException}, in both dialects.
 */
export async function raiseForStatus(response: Response): Promise<void> {
  if (response.ok) {
    return;
  }

  const body = await response.text();
  const retry = retryAfter(response);

  if (response.status === 424) {
    const message =
      extractErrorMessage(body) ?? `InferHub request failed with status ${response.status}.`;
    throw new InferHubRetrievalException(response.status, message, body, {
      retryAfter: retry,
    });
  }

  const openai = parseOpenAiEnvelope(body);
  if (openai) {
    throw new InferHubOpenAiException(response.status, openai.message, body, {
      errorCode: openai.code,
      param: openai.param,
      errorType: openai.errorType,
      retryAfter: retry,
    });
  }

  const message =
    extractErrorMessage(body) ?? `InferHub request failed with status ${response.status}.`;
  throw new InferHubError(response.status, message, body, { retryAfter: retry });
}

/**
 * One line of an NDJSON stream, or `undefined` for a blank line to skip. Throws
 * {@link InferHubError} on a terminal error chunk (`{"error": ..., "done": true}`) so a caller's
 * loop stops with a clear exception instead of hanging or silently finishing early (root
 * testing-discipline rule, corpus case `mid-stream-error-terminates-not-hangs`).
 */
export function parseNdjsonLine(line: string): JsonDict | undefined {
  if (!line.trim()) {
    return undefined;
  }
  const chunk = JSON.parse(line) as JsonDict;
  const error = chunk.error;
  if (error) {
    throw new InferHubError(200, String(error), line);
  }
  return chunk;
}

/** Which node or `provider:<id>` answered — surfaced, never interpreted (root `CLAUDE.md` rule
 * 8). This client does not route, retry elsewhere or prefer on it. */
export function readServedBy(response: Response): string | undefined {
  const value = response.headers.get("X-InferHub-Served-By");
  if (!value) {
    return undefined;
  }
  const trimmed = value.trim();
  return trimmed || undefined;
}

/**
 * `X-InferHub-Sources` arrives as a JSON array, but a real hub has also sent it comma-separated
 * (`spec/README.md` calls this the conformance corpus's first case) — both shapes are parsed here
 * even though v0.1.0 has no way yet to opt into retrieval (D3 non-goal note).
 */
export function readSourceIds(response: Response): string[] | undefined {
  const raw = response.headers.get("X-InferHub-Sources");
  if (raw === null) {
    return undefined;
  }
  const trimmed = raw.trim();
  if (!trimmed) {
    return [];
  }
  try {
    const parsed: unknown = JSON.parse(trimmed);
    if (Array.isArray(parsed)) {
      return parsed
        .filter((item) => item !== null && item !== undefined && String(item) !== "")
        .map((item) => String(item));
    }
  } catch {
    // fall through to the comma-separated form
  }
  return trimmed
    .split(",")
    .map((part) => part.trim())
    .filter((part) => part.length > 0);
}
