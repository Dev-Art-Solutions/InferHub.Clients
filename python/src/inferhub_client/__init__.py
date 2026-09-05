"""inferhub-client — a small, typed Python client for InferHub.

>>> from inferhub_client import InferHubClient, ChatMessage, ChatRequest
>>> with InferHubClient("http://localhost:5080/", api_key="sk-...") as client:
...     answer = client.chat(ChatRequest(model="llama3", messages=[ChatMessage("user", "hi")]))
...     print(answer.message.content)
"""

from ._async_client import AsyncInferHubClient
from ._client import InferHubClient
from ._exceptions import InferHubError
from ._models import (
    ChatMessage,
    ChatRequest,
    ChatResponse,
    EmbeddingsRequest,
    EmbeddingsResponse,
    EmbedRequest,
    EmbedResponse,
    GenerateRequest,
    GenerateResponse,
    ModelInfo,
    StatusResponse,
    TagsResponse,
)
from ._version import __version__

__all__ = [
    "__version__",
    "InferHubClient",
    "AsyncInferHubClient",
    "InferHubError",
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
]
