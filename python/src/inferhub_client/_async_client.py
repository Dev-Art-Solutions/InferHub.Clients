from __future__ import annotations

from typing import AsyncIterator, Optional

import httpx

from ._base import (
    DEFAULT_BASE_URL,
    build_headers,
    parse_ndjson_line,
    raise_for_status,
    read_served_by,
    read_source_ids,
)
from ._exceptions import InferHubError
from ._models import (
    ChatRequest,
    ChatResponse,
    EmbedRequest,
    EmbedResponse,
    EmbeddingsRequest,
    EmbeddingsResponse,
    GenerateRequest,
    GenerateResponse,
    StatusResponse,
    TagsResponse,
)


class AsyncInferHubClient:
    """Async client for an InferHub coordinator (or a solo node — same address, same client, see
    ``python/README.md``). Covers the Ollama-dialect core surface: chat, generate (blocking and
    streaming), embeddings, model listing, status and health.
    """

    def __init__(
        self,
        base_url: str = DEFAULT_BASE_URL,
        api_key: Optional[str] = None,
        *,
        timeout: float = 100.0,
        http_client: Optional[httpx.AsyncClient] = None,
    ) -> None:
        self._owns_client = http_client is None
        self._http = http_client or httpx.AsyncClient(
            base_url=base_url, headers=build_headers(api_key), timeout=timeout
        )

    async def __aenter__(self) -> "AsyncInferHubClient":
        return self

    async def __aexit__(self, *exc_info: object) -> None:
        await self.aclose()

    async def aclose(self) -> None:
        if self._owns_client:
            await self._http.aclose()

    async def list_models(self) -> TagsResponse:
        """``GET /api/tags`` — models advertised by the mesh."""
        response = await self._http.get("api/tags")
        raise_for_status(response)
        return TagsResponse.from_json(response.json())

    async def chat(self, request: ChatRequest) -> ChatResponse:
        """Blocking chat — ``POST /api/chat`` with ``stream:false``."""
        request.stream = False
        response = await self._http.post("api/chat", json=request.to_json())
        raise_for_status(response)
        result = ChatResponse.from_json(response.json())
        result.served_by = read_served_by(response)
        result.source_ids = read_source_ids(response)
        return result

    async def chat_stream(self, request: ChatRequest) -> AsyncIterator[ChatResponse]:
        """Streaming chat — ``POST /api/chat`` with ``stream:true``. Yields one
        :class:`ChatResponse` per NDJSON line; a terminal error chunk raises
        :class:`InferHubError` instead of the iterator hanging or ending quietly."""
        request.stream = True
        async with self._http.stream(
            "POST", "api/chat", json=request.to_json()
        ) as response:
            raise_for_status(response)
            served_by = read_served_by(response)
            source_ids = read_source_ids(response)
            async for line in response.aiter_lines():
                chunk = parse_ndjson_line(line)
                if chunk is None:
                    continue
                result = ChatResponse.from_json(chunk)
                result.served_by = served_by
                result.source_ids = source_ids
                yield result
                if result.done:
                    return

    async def generate(self, request: GenerateRequest) -> GenerateResponse:
        """Blocking generate — ``POST /api/generate`` with ``stream:false``."""
        request.stream = False
        response = await self._http.post("api/generate", json=request.to_json())
        raise_for_status(response)
        result = GenerateResponse.from_json(response.json())
        result.served_by = read_served_by(response)
        result.source_ids = read_source_ids(response)
        return result

    async def generate_stream(
        self, request: GenerateRequest
    ) -> AsyncIterator[GenerateResponse]:
        """Streaming generate — ``POST /api/generate`` with ``stream:true``."""
        request.stream = True
        async with self._http.stream(
            "POST", "api/generate", json=request.to_json()
        ) as response:
            raise_for_status(response)
            served_by = read_served_by(response)
            source_ids = read_source_ids(response)
            async for line in response.aiter_lines():
                chunk = parse_ndjson_line(line)
                if chunk is None:
                    continue
                result = GenerateResponse.from_json(chunk)
                result.served_by = served_by
                result.source_ids = source_ids
                yield result
                if result.done:
                    return

    async def embed(self, request: EmbedRequest) -> EmbedResponse:
        """``POST /api/embed`` — batch embeddings. An empty vector list on a 200 is treated as a
        malformed response and raised, never silently returned."""
        response = await self._http.post("api/embed", json=request.to_json())
        raise_for_status(response)
        result = EmbedResponse.from_json(response.json())
        if not result.embeddings:
            raise InferHubError(response.status_code, "embed response had no vectors")
        return result

    async def embed_legacy(self, request: EmbeddingsRequest) -> EmbeddingsResponse:
        """``POST /api/embeddings`` — the legacy single-input endpoint. Prefer :meth:`embed`."""
        response = await self._http.post("api/embeddings", json=request.to_json())
        raise_for_status(response)
        result = EmbeddingsResponse.from_json(response.json())
        if not result.embedding:
            raise InferHubError(
                response.status_code, "embeddings response had no vector"
            )
        return result

    async def get_status(self) -> StatusResponse:
        """``GET /api/status`` — coordinator/fleet snapshot."""
        response = await self._http.get("api/status")
        raise_for_status(response)
        return StatusResponse.from_json(response.json())

    async def ping(self) -> bool:
        """``GET /health`` — ``True`` on 2xx, ``False`` otherwise. Never raises for a non-success
        status; raises only on a transport error."""
        response = await self._http.get("health")
        return response.is_success
