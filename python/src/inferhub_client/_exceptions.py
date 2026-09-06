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
