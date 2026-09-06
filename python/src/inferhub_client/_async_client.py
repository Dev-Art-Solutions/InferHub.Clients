from __future__ import annotations

from typing import AsyncIterator, Optional

import httpx

from ._base import (
    DEFAULT_BASE_URL,
    build_headers,
    build_retrieval_headers,
    parse_ndjson_line,
    raise_for_status,
    read_served_by,
    read_source_ids,
)
from ._corpus import _AsyncCorpusMethodsMixin
from ._exceptions import InferHubError
from ._models import (
    ChatRequest,
    ChatResponse,
    EmbeddingsRequest,
    EmbeddingsResponse,
    EmbedRequest,
    EmbedResponse,
    GenerateRequest,
    GenerateResponse,
    RetrievalOptions,
    StatusResponse,
    TagsResponse,
    VectorMatch,
    VectorQuery,
    VectorRecord,
    VectorUpsert,
)


class AsyncInferHubClient(_AsyncCorpusMethodsMixin):
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

    async def chat(
        self, request: ChatRequest, *, retrieval: Optional[RetrievalOptions] = None
    ) -> ChatResponse:
        """Blocking chat — ``POST /api/chat`` with ``stream:false``. ``retrieval`` sets the
        ``X-InferHub-Retrieve*`` headers for this call only; a 424 raises
        :class:`~inferhub_client.InferHubRetrievalException`."""
        request.stream = False
        response = await self._http.post(
            "api/chat",
            json=request.to_json(),
            headers=build_retrieval_headers(retrieval),
        )
        raise_for_status(response)
        result = ChatResponse.from_json(response.json())
        result.served_by = read_served_by(response)
        result.source_ids = read_source_ids(response)
        return result

    async def chat_stream(
        self, request: ChatRequest, *, retrieval: Optional[RetrievalOptions] = None
    ) -> AsyncIterator[ChatResponse]:
        """Streaming chat — ``POST /api/chat`` with ``stream:true``. Yields one
        :class:`ChatResponse` per NDJSON line; a terminal error chunk raises
        :class:`InferHubError` instead of the iterator hanging or ending quietly."""
        request.stream = True
        async with self._http.stream(
            "POST",
            "api/chat",
            json=request.to_json(),
            headers=build_retrieval_headers(retrieval),
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

    async def generate(
        self, request: GenerateRequest, *, retrieval: Optional[RetrievalOptions] = None
    ) -> GenerateResponse:
        """Blocking generate — ``POST /api/generate`` with ``stream:false``."""
        request.stream = False
        response = await self._http.post(
            "api/generate",
            json=request.to_json(),
            headers=build_retrieval_headers(retrieval),
        )
        raise_for_status(response)
        result = GenerateResponse.from_json(response.json())
        result.served_by = read_served_by(response)
        result.source_ids = read_source_ids(response)
        return result

    async def generate_stream(
        self, request: GenerateRequest, *, retrieval: Optional[RetrievalOptions] = None
    ) -> AsyncIterator[GenerateResponse]:
        """Streaming generate — ``POST /api/generate`` with ``stream:true``."""
        request.stream = True
        async with self._http.stream(
            "POST",
            "api/generate",
            json=request.to_json(),
            headers=build_retrieval_headers(retrieval),
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

    # -- Vector data-plane (phase 17) --------------------------------------------------------

    async def upsert(self, collection: str, upsert: VectorUpsert) -> VectorRecord:
        """``POST /api/vector/{collection}/upsert``."""
        response = await self._http.post(
            f"api/vector/{collection}/upsert", json=upsert.to_json()
        )
        raise_for_status(response)
        return VectorRecord.from_json(response.json())

    async def query(self, collection: str, query: VectorQuery) -> list:
        """``POST /api/vector/{collection}/query``."""
        response = await self._http.post(
            f"api/vector/{collection}/query", json=query.to_json()
        )
        raise_for_status(response)
        return [VectorMatch.from_json(m) for m in response.json().get("matches") or []]

    async def retrieve(self, collection: str, query: VectorQuery) -> list:
        """``POST /api/vector/{collection}/retrieve`` — same shape as :meth:`query`, the
        RAG-oriented route name the hub also answers on."""
        response = await self._http.post(
            f"api/vector/{collection}/retrieve", json=query.to_json()
        )
        raise_for_status(response)
        return [VectorMatch.from_json(m) for m in response.json().get("matches") or []]

    async def get_record(self, collection: str, id: str) -> Optional[VectorRecord]:
        """``GET /api/vector/{collection}/{id}`` — ``None`` on 404, never raised."""
        response = await self._http.get(f"api/vector/{collection}/{id}")
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return VectorRecord.from_json(response.json())

    async def delete_record(self, collection: str, id: str) -> bool:
        """``DELETE /api/vector/{collection}/{id}`` — ``True`` iff a record was actually deleted."""
        response = await self._http.delete(f"api/vector/{collection}/{id}")
        if response.status_code == 404:
            return False
        raise_for_status(response)
        return True
