"""inferhub-client — a small, typed Python client for InferHub.

>>> from inferhub_client import InferHubClient, ChatMessage, ChatRequest
>>> with InferHubClient("http://localhost:5080/", api_key="sk-...") as client:
...     answer = client.chat(ChatRequest(model="llama3", messages=[ChatMessage("user", "hi")]))
...     print(answer.message.content)
"""

from ._async_client import AsyncInferHubClient
from ._client import InferHubClient
from ._exceptions import InferHubError, InferHubRetrievalException
from ._models import (
    ChatMessage,
    ChatRequest,
    ChatResponse,
    DocumentChunk,
    DocumentChunksResponse,
    DocumentDeletion,
    DocumentSummary,
    EmbeddingsRequest,
    EmbeddingsResponse,
    EmbedRequest,
    EmbedResponse,
    FileDocument,
    GenerateRequest,
    GenerateResponse,
    IngestResult,
    ModelInfo,
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
)
from ._version import __version__

__all__ = [
    "__version__",
    "InferHubClient",
    "AsyncInferHubClient",
    "InferHubError",
    "InferHubRetrievalException",
    "ChatMessage",
    "ChatRequest",
    "ChatResponse",
    "GenerateRequest",
    "GenerateResponse",
    "EmbedRequest",
    "EmbedResponse",
    "EmbeddingsRequest",
    "EmbeddingsResponse",
    "ModelInfo",
    "TagsResponse",
    "StatusResponse",
    "RetrievalOptions",
    "VectorUpsert",
    "VectorQuery",
    "VectorMatch",
    "VectorRecord",
    "TextDocument",
    "FileDocument",
    "IngestResult",
    "DocumentSummary",
    "DocumentChunk",
    "DocumentChunksResponse",
    "DocumentDeletion",
    "SearchRequest",
    "SearchResponse",
    "SearchHit",
]
