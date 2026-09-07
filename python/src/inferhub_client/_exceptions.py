from __future__ import annotations

from typing import Optional


class InferHubError(Exception):
    """Raised when the coordinator (or a solo node) answers a non-success HTTP status.

    Carries the raw status code so a caller can distinguish 404 (model or collection missing),
    401/403 (auth), 501 (a backend that structurally cannot serve a capability) from 503 (a
    capability an operator disabled, temporary, and worth the ``retry_after`` seconds).
    """

    def __init__(
        self,
        status_code: int,
        message: str,
        response_body: str = "",
        *,
        retry_after: Optional[float] = None,
    ) -> None:
        super().__init__(message)
        self.status_code = status_code
        self.message = message
        self.response_body = response_body
        self.retry_after = retry_after

    def __repr__(self) -> str:  # pragma: no cover - cosmetic
        return (
            f"InferHubError(status_code={self.status_code!r}, message={self.message!r})"
        )


class InferHubRetrievalException(InferHubError):
    """Raised on HTTP 424 — retrieval was asked for (``X-InferHub-Retrieve``) and is unavailable.

    A distinct type from the base :class:`InferHubError` because 424 is not "the model is
    missing" (404): the chat/generate call itself could have succeeded, but the retrieval step it
    depended on could not. A caller catching this specifically can retry without retrieval; one
    that catches only :class:`InferHubError` still works, since this is a subclass (conformance
    case ``424-is-not-404``).
    """


class InferHubOpenAiException(InferHubError):
    """Raised when a ``/v1/*`` route (or a route that reuses the same envelope, e.g.
    ``/api/images/jobs``) answers ``{"error":{"message":...,"type":...,"param":...,"code":...}}``
    (root ``CLAUDE.md`` rule 9 — *which envelope arrived* decides the exception type, never which
    method was called; ``raise_for_status`` sniffs the body's ``error`` shape, so this fires for
    audio, images and image-job routes alike without either surface hard-coding a type).

    ``error_code`` is the machine-readable string worth catching by name — e.g.
    ``capability_unavailable`` (fleet has the model, nobody is currently serving it — retryable,
    carries :attr:`InferHubError.retry_after`), ``job_expired``, ``job_not_ready``,
    ``image_not_found``. A plain :class:`InferHubError` catch still works, since this is a
    subclass.
    """

    def __init__(
        self,
        status_code: int,
        message: str,
        response_body: str = "",
        *,
        error_code: Optional[str] = None,
        param: Optional[str] = None,
        error_type: Optional[str] = None,
        retry_after: Optional[float] = None,
    ) -> None:
        super().__init__(status_code, message, response_body, retry_after=retry_after)
        self.error_code = error_code
        self.param = param
        self.error_type = error_type
