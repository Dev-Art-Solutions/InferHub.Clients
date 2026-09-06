"""Ingestion and search — ``/api/collections/{collection}/**``. A separate module (phase 17 D3)
mixed into both :class:`~inferhub_client._client.InferHubClient` and
:class:`~inferhub_client._async_client.AsyncInferHubClient` rather than duplicated: the corpus
surface is seven methods with multipart upload and its own error shape, and putting it beside
chat/generate/vectors would leave ``_client.py`` covering three unrelated planes.
"""

from __future__ import annotations

from typing import Optional

import httpx

from ._base import raise_for_status
from ._models import (
    DocumentChunksResponse,
    DocumentDeletion,
    DocumentSummary,
    FileDocument,
    IngestResult,
    SearchRequest,
    SearchResponse,
    TextDocument,
)


def _ingest_result_or_raise(response: httpx.Response) -> IngestResult:
    """A ``partial`` ingest is an HTTP 500 **with an ``IngestResult`` body**, returned rather than
    raised (D3, conformance case ``partial-ingest-is-a-500-with-a-body-not-thrown``). Anything else
    non-2xx — a genuine ``{"error": ...}`` envelope, or a body with neither ``documentId`` nor
    ``status`` — still raises normally."""

    if not response.is_success:
        try:
            data = response.json()
        except ValueError:
            data = None
        if isinstance(data, dict) and IngestResult.looks_like_one(data):
            return IngestResult.from_json(data)
        raise_for_status(response)
    return IngestResult.from_json(response.json())


class _CorpusMethodsMixin:
    """Sync corpus methods. Mixed into :class:`InferHubClient`; expects ``self._http`` to be an
    ``httpx.Client``."""

    _http: httpx.Client

    def ingest_text(self, collection: str, document: TextDocument) -> IngestResult:
        """``POST /api/collections/{collection}/documents`` with a JSON body."""
        response = self._http.post(
            f"api/collections/{collection}/documents", json=document.to_json()
        )
        return _ingest_result_or_raise(response)

    def ingest_file(self, collection: str, document: FileDocument) -> IngestResult:
        """``POST /api/collections/{collection}/documents`` as multipart. The file field is last
        (D4) — some multipart parsers are order-sensitive and this matches the C# client's own
        ``MultipartFormDataContent`` field order."""
        data: dict = {"id": document.id}
        if document.metadata is not None:
            data["metadata"] = document.metadata
        files = {"file": (document.filename, document.stream, document.content_type)}
        response = self._http.post(
            f"api/collections/{collection}/documents", data=data, files=files
        )
        return _ingest_result_or_raise(response)

    def list_documents(self, collection: str) -> list:
        response = self._http.get(f"api/collections/{collection}/documents")
        raise_for_status(response)
        return [
            DocumentSummary.from_json(d) for d in response.json().get("documents") or []
        ]

    def get_document(
        self, collection: str, document_id: str
    ) -> Optional[DocumentSummary]:
        response = self._http.get(
            f"api/collections/{collection}/documents/{document_id}"
        )
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return DocumentSummary.from_json(response.json())

    def get_chunks(self, collection: str, document_id: str) -> DocumentChunksResponse:
        response = self._http.get(
            f"api/collections/{collection}/documents/{document_id}/chunks"
        )
        raise_for_status(response)
        return DocumentChunksResponse.from_json(response.json())

    def delete_document(
        self, collection: str, document_id: str
    ) -> Optional[DocumentDeletion]:
        response = self._http.delete(
            f"api/collections/{collection}/documents/{document_id}"
        )
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return DocumentDeletion.from_json(response.json())

    def search(self, collection: str, query, top_k: int = 10) -> SearchResponse:
        """``search(collection, "a question")`` or ``search(collection, SearchRequest(...))`` —
        Python has no method overloads, so the string form is a second accepted type rather than a
        second method name."""
        request = (
            query
            if isinstance(query, SearchRequest)
            else SearchRequest(query=query, top_k=top_k)
        )
        response = self._http.post(
            f"api/collections/{collection}/search", json=request.to_json()
        )
        raise_for_status(response)
        return SearchResponse.from_json(response.json())


class _AsyncCorpusMethodsMixin:
    """Async twin of :class:`_CorpusMethodsMixin`. Mixed into :class:`AsyncInferHubClient`;
    expects ``self._http`` to be an ``httpx.AsyncClient``."""

    _http: httpx.AsyncClient

    async def ingest_text(
        self, collection: str, document: TextDocument
    ) -> IngestResult:
        response = await self._http.post(
            f"api/collections/{collection}/documents", json=document.to_json()
        )
        return _ingest_result_or_raise(response)

    async def ingest_file(
        self, collection: str, document: FileDocument
    ) -> IngestResult:
        data: dict = {"id": document.id}
        if document.metadata is not None:
            data["metadata"] = document.metadata
        files = {"file": (document.filename, document.stream, document.content_type)}
        response = await self._http.post(
            f"api/collections/{collection}/documents", data=data, files=files
        )
        return _ingest_result_or_raise(response)

    async def list_documents(self, collection: str) -> list:
        response = await self._http.get(f"api/collections/{collection}/documents")
        raise_for_status(response)
        return [
            DocumentSummary.from_json(d) for d in response.json().get("documents") or []
        ]

    async def get_document(
        self, collection: str, document_id: str
    ) -> Optional[DocumentSummary]:
        response = await self._http.get(
            f"api/collections/{collection}/documents/{document_id}"
        )
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return DocumentSummary.from_json(response.json())

    async def get_chunks(
        self, collection: str, document_id: str
    ) -> DocumentChunksResponse:
        response = await self._http.get(
            f"api/collections/{collection}/documents/{document_id}/chunks"
        )
        raise_for_status(response)
        return DocumentChunksResponse.from_json(response.json())

    async def delete_document(
        self, collection: str, document_id: str
    ) -> Optional[DocumentDeletion]:
        response = await self._http.delete(
            f"api/collections/{collection}/documents/{document_id}"
        )
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return DocumentDeletion.from_json(response.json())

    async def search(self, collection: str, query, top_k: int = 10) -> SearchResponse:
        request = (
            query
            if isinstance(query, SearchRequest)
            else SearchRequest(query=query, top_k=top_k)
        )
        response = await self._http.post(
            f"api/collections/{collection}/search", json=request.to_json()
        )
        raise_for_status(response)
        return SearchResponse.from_json(response.json())
