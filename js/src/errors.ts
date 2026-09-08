/**
 * Error model — mirrors python `_exceptions.py`'s three types (itself a port of the C# client's).
 * Root `CLAUDE.md` rule 9: which envelope arrived decides the exception type, never which method
 * was called. `/api/*` answers `{"error":"..."}` (a plain string); `/v1/*` and routes that reuse
 * its shape answer `{"error":{"message":...,"type":...,"param":...,"code":...}}`. A 424 is always
 * {@link InferHubRetrievalException} in either dialect.
 */

/** Raised when the coordinator (or a solo node) answers a non-success HTTP status. */
export class InferHubError extends Error {
  /** The raw HTTP status code — 404 (model missing), 401/403 (auth), 503 (temporary, see
   * {@link retryAfter}), etc. */
  readonly statusCode: number;
  /** The raw response body, for a caller who wants more than `message`. */
  readonly responseBody: string;
  /** Seconds to wait before retrying, from a `Retry-After` header — `undefined` when the hub did
   * not send one (an HTTP-date form exists but the hub always writes delta-seconds). */
  readonly retryAfter?: number;

  constructor(
    statusCode: number,
    message: string,
    responseBody = "",
    options?: { retryAfter?: number },
  ) {
    super(message);
    this.name = "InferHubError";
    this.statusCode = statusCode;
    this.responseBody = responseBody;
    this.retryAfter = options?.retryAfter;
    Object.setPrototypeOf(this, InferHubError.prototype);
  }
}

/**
 * Raised on HTTP 424 — retrieval was asked for (`X-InferHub-Retrieve`) and is unavailable. A
 * distinct type from {@link InferHubError} because the chat/generate call itself could have
 * succeeded; only the retrieval step it depended on could not. A `catch (e instanceof
 * InferHubError)` still matches, since this is a subclass.
 */
export class InferHubRetrievalException extends InferHubError {
  constructor(
    statusCode: number,
    message: string,
    responseBody = "",
    options?: { retryAfter?: number },
  ) {
    super(statusCode, message, responseBody, options);
    this.name = "InferHubRetrievalException";
    Object.setPrototypeOf(this, InferHubRetrievalException.prototype);
  }
}

/**
 * Raised when a `/v1/*` route (or a route reusing the same envelope) answers
 * `{"error":{"message":...,"type":...,"param":...,"code":...}}`. `errorCode` is the
 * machine-readable string worth catching by name (e.g. `capability_unavailable`, retryable via
 * {@link InferHubError.retryAfter}).
 */
export class InferHubOpenAiException extends InferHubError {
  readonly errorCode?: string;
  readonly param?: string;
  readonly errorType?: string;

  constructor(
    statusCode: number,
    message: string,
    responseBody = "",
    options?: {
      errorCode?: string;
      param?: string;
      errorType?: string;
      retryAfter?: number;
    },
  ) {
    super(statusCode, message, responseBody, { retryAfter: options?.retryAfter });
    this.name = "InferHubOpenAiException";
    this.errorCode = options?.errorCode;
    this.param = options?.param;
    this.errorType = options?.errorType;
    Object.setPrototypeOf(this, InferHubOpenAiException.prototype);
  }
}
