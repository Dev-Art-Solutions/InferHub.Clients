"""Shared, I/O-free plumbing used by both :class:`InferHubClient` (sync) and
:class:`AsyncInferHubClient` (async): headers, error mapping, and NDJSON chunk parsing. Kept out of
either client so the two stay thin façades over the same rules rather than two copies that drift —
see ``python/README.md`` on why this is two classes and not one client with a sync-over-async shim.
"""

from __future__ import annotations

import json
from typing import Any, Dict, Optional

import httpx

from ._exceptions import InferHubError, InferHubRetrievalException
from ._models import RetrievalOptions

DEFAULT_BASE_URL = "http://localhost:5080/"


def build_headers(api_key: Optional[str]) -> Dict[str, str]:
    """No default ``Content-Type`` here (phase 17 finding): a client-level default header wins
    over what ``httpx`` would otherwise compute per request, so a fixed ``application/json``
    default silently broke multipart ingestion — ``httpx`` already sets the right content type for
    ``json=`` and ``files=`` calls on its own when neither the client nor the call sets one."""

    headers: Dict[str, str] = {}
    if api_key:
        headers["Authorization"] = f"Bearer {api_key}"
    return headers


def build_retrieval_headers(options: Optional[RetrievalOptions]) -> Dict[str, str]:
    """The five ``X-InferHub-Retrieve*``/``X-InferHub-Rerank`` headers for one chat/generate call.
    A call-scoped concern kept off the request dataclass on purpose (phase 17 D1) — retrieval
    applies to both ``chat`` and ``generate``, and folding it into either request's ``to_json()``
    would mean the body serializer has to know to exclude a header-only field."""

    if options is None:
        return {}
    headers: Dict[str, str] = {"X-InferHub-Retrieve": options.collection}
    if options.k is not None:
        headers["X-InferHub-Retrieve-K"] = str(options.k)
    if options.model is not None:
        headers["X-InferHub-Retrieve-Model"] = options.model
    if options.mode is not None:
        headers["X-InferHub-Retrieve-Mode"] = options.mode
    if options.rerank is not None:
        headers["X-InferHub-Rerank"] = str(options.rerank).lower()
    return headers


def _extract_error_message(body: str) -> Optional[str]:
    """The Ollama dialect answers ``{"error": "..."}"``. A non-JSON or differently-shaped body
    falls back to the raw text, same as the C# client's ``TryExtractErrorMessage``."""

    if not body.strip():
        return None
    try:
        parsed = json.loads(body)
    except (json.JSONDecodeError, ValueError):
        return body
    if isinstance(parsed, dict) and isinstance(parsed.get("error"), str):
        return parsed["error"]
    return body


def _retry_after(response: httpx.Response) -> Optional[float]:
    header = response.headers.get("Retry-After")
    if not header:
        return None
    try:
        return float(header)
    except ValueError:
        return None  # An HTTP-date form exists but the hub always writes delta-seconds.


def raise_for_status(response: httpx.Response) -> None:
    if response.is_success:
        return

    body = response.text
    message = _extract_error_message(body) or (
        f"InferHub request failed with status {response.status_code}."
    )
    error_cls = (
        InferHubRetrievalException if response.status_code == 424 else InferHubError
    )
    raise error_cls(
        response.status_code, message, body, retry_after=_retry_after(response)
    )


def parse_ndjson_line(line: str) -> Optional[Dict[str, Any]]:
    """One line of an NDJSON stream, or ``None`` for a blank line to skip. Raises
    :class:`InferHubError` on a terminal error chunk (``{"error": ..., "done": true}``) so a caller's
    loop stops with a clear exception instead of hanging or silently finishing early."""

    if not line.strip():
        return None

    chunk = json.loads(line)
    error = chunk.get("error")
    if error:
        raise InferHubError(200, error, line)
    return chunk


def read_served_by(response: httpx.Response) -> Optional[str]:
    """Which node or ``provider:<id>`` answered — surfaced, never interpreted. This client does
    not route, retry elsewhere or prefer on it (root ``CLAUDE.md`` rule 8); reading it here is what
    lets a caller log or display it without this library making a decision on its behalf."""

    value = response.headers.get("X-InferHub-Served-By")
    return value.strip() or None if value else None


def read_source_ids(response: httpx.Response) -> Optional[list]:
    """``X-InferHub-Sources`` arrives as a JSON array, but a real hub has also sent it
    comma-separated — ``spec/README.md`` calls this the conformance corpus's first case, and both
    shapes are parsed here even though ``v0.1.0`` has no way yet to opt into retrieval (phase 17):
    the header is part of the core response contract, and a caller reading it manually should not
    have to wait for this client to grow RAG headers first."""

    raw = response.headers.get("X-InferHub-Sources")
    if raw is None:
        return None
    raw = raw.strip()
    if not raw:
        return []
    try:
        parsed = json.loads(raw)
        if isinstance(parsed, list):
            return [
                str(item) for item in parsed if item is not None and str(item) != ""
            ]
    except (json.JSONDecodeError, ValueError):
        pass
    return [part.strip() for part in raw.split(",") if part.strip()]
