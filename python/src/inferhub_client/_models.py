"""Typed request/response shapes for the Ollama-dialect core surface.

Dataclasses, not pydantic (root ``CLAUDE.md`` rule 2 / roadmap-polyglot-clients D5): the wire is
small and stable enough that a validation library is somebody else's dependency war inherited by
every consumer. Every response type keeps an ``extra`` dict for fields the hub sends that this
version does not know about yet — the Python equivalent of the C# client's ``[JsonExtensionData]``
bag — so a caller reaching for a brand-new field is never blocked on a new release of this package.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Union

JsonDict = Dict[str, Any]

#: ``vector`` | ``keyword`` | ``hybrid`` — the hub's own retrieval/search mode strings, passed
#: through as plain ``str`` rather than an enum (phase-16 D2's "extra is the extension bag"
#: precedent: a new mode the hub adds should not need a new client release to be reachable).
RetrievalMode = str

_KNOWN_MESSAGE_FIELDS = {"role", "content", "images", "tool_calls"}


@dataclass
class ChatMessage:
    """One message in a chat request or response."""

    role: str
    content: str = ""
    images: Optional[List[str]] = None
    tool_calls: Optional[List[JsonDict]] = None
    extra: JsonDict = field(default_factory=dict)

    def to_json(self) -> JsonDict:
        body: JsonDict = {"role": self.role, "content": self.content}
        if self.images is not None:
            body["images"] = self.images
        if self.tool_calls is not None:
            body["tool_calls"] = self.tool_calls
        body.update(self.extra)
        return body

    @classmethod
    def from_json(cls, data: JsonDict) -> "ChatMessage":
        return cls(
            role=data.get("role", ""),
            content=data.get("content", ""),
            images=data.get("images"),
            tool_calls=data.get("tool_calls"),
            extra={k: v for k, v in data.items() if k not in _KNOWN_MESSAGE_FIELDS},
        )


@dataclass
class ChatRequest:
    """``POST /api/chat``. ``extra`` merges straight into the top-level body, untouched — the
    passthrough that keeps every Ollama option (``options``, ``format``, ``keep_alive``, tool
    definitions) reachable without this client typing each one (roadmap-polyglot-clients,
    "typed request builders for every option" is a rejected non-goal, same as the C# client)."""

    model: str
    messages: List[ChatMessage] = field(default_factory=list)
    stream: bool = False
    options: Optional[JsonDict] = None
    format: Optional[Union[str, JsonDict]] = None
    keep_alive: Optional[str] = None
    extra: JsonDict = field(default_factory=dict)

    def to_json(self) -> JsonDict:
        body: JsonDict = {
            "model": self.model,
            "messages": [m.to_json() for m in self.messages],
            "stream": self.stream,
        }
        if self.options is not None:
            body["options"] = self.options
        if self.format is not None:
            body["format"] = self.format
        if self.keep_alive is not None:
            body["keep_alive"] = self.keep_alive
        body.update(self.extra)
        return body


_KNOWN_CHAT_RESPONSE_FIELDS = {
    "model",
    "created_at",
    "message",
    "done",
    "done_reason",
    "total_duration",
    "load_duration",
    "prompt_eval_count",
    "prompt_eval_duration",
    "eval_count",
    "eval_duration",
    "error",
}


@dataclass
class ChatResponse:
    """A blocking answer, or one NDJSON chunk of a streamed one."""

    model: str = ""
    created_at: Optional[str] = None
    message: Optional[ChatMessage] = None
    done: Optional[bool] = None
    done_reason: Optional[str] = None
    total_duration: Optional[int] = None
    load_duration: Optional[int] = None
    prompt_eval_count: Optional[int] = None
    prompt_eval_duration: Optional[int] = None
    eval_count: Optional[int] = None
    eval_duration: Optional[int] = None
    error: Optional[str] = None
    extra: JsonDict = field(default_factory=dict)
    # Set from response headers, not the body — never part of round-tripping the JSON.
    served_by: Optional[str] = None
    source_ids: Optional[List[str]] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "ChatResponse":
        message = data.get("message")
        return cls(
            model=data.get("model", ""),
            created_at=data.get("created_at"),
            message=ChatMessage.from_json(message) if message is not None else None,
            done=data.get("done"),
            done_reason=data.get("done_reason"),
            total_duration=data.get("total_duration"),
            load_duration=data.get("load_duration"),
            prompt_eval_count=data.get("prompt_eval_count"),
            prompt_eval_duration=data.get("prompt_eval_duration"),
            eval_count=data.get("eval_count"),
            eval_duration=data.get("eval_duration"),
            error=data.get("error"),
            extra={
                k: v for k, v in data.items() if k not in _KNOWN_CHAT_RESPONSE_FIELDS
            },
        )


@dataclass
class GenerateRequest:
    """``POST /api/generate``. Same extension-bag contract as :class:`ChatRequest`."""

    model: str
    prompt: str = ""
    stream: bool = False
    options: Optional[JsonDict] = None
    format: Optional[Union[str, JsonDict]] = None
    keep_alive: Optional[str] = None
    extra: JsonDict = field(default_factory=dict)

    def to_json(self) -> JsonDict:
        body: JsonDict = {
            "model": self.model,
            "prompt": self.prompt,
            "stream": self.stream,
        }
        if self.options is not None:
            body["options"] = self.options
        if self.format is not None:
            body["format"] = self.format
        if self.keep_alive is not None:
            body["keep_alive"] = self.keep_alive
        body.update(self.extra)
        return body


_KNOWN_GENERATE_RESPONSE_FIELDS = {
    "model",
    "created_at",
    "response",
    "done",
    "done_reason",
    "context",
    "total_duration",
    "load_duration",
    "prompt_eval_count",
    "prompt_eval_duration",
    "eval_count",
    "eval_duration",
    "error",
}


@dataclass
class GenerateResponse:
    model: str = ""
    created_at: Optional[str] = None
    response: str = ""
    done: Optional[bool] = None
    done_reason: Optional[str] = None
    context: Optional[List[int]] = None
    total_duration: Optional[int] = None
    load_duration: Optional[int] = None
    prompt_eval_count: Optional[int] = None
    prompt_eval_duration: Optional[int] = None
    eval_count: Optional[int] = None
    eval_duration: Optional[int] = None
    error: Optional[str] = None
    extra: JsonDict = field(default_factory=dict)
    served_by: Optional[str] = None
    source_ids: Optional[List[str]] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "GenerateResponse":
        return cls(
            model=data.get("model", ""),
            created_at=data.get("created_at"),
            response=data.get("response", ""),
            done=data.get("done"),
            done_reason=data.get("done_reason"),
            context=data.get("context"),
            total_duration=data.get("total_duration"),
            load_duration=data.get("load_duration"),
            prompt_eval_count=data.get("prompt_eval_count"),
            prompt_eval_duration=data.get("prompt_eval_duration"),
            eval_count=data.get("eval_count"),
            eval_duration=data.get("eval_duration"),
            error=data.get("error"),
            extra={
                k: v
                for k, v in data.items()
                if k not in _KNOWN_GENERATE_RESPONSE_FIELDS
            },
        )


@dataclass
class EmbedRequest:
    """``POST /api/embed`` — the modern batch endpoint. ``input`` is a single string or a list."""

    model: str
    input: Union[str, List[str]]

    def to_json(self) -> JsonDict:
        return {"model": self.model, "input": self.input}

    @classmethod
    def from_text(cls, model: str, text: str) -> "EmbedRequest":
        return cls(model=model, input=text)

    @classmethod
    def from_texts(cls, model: str, texts: List[str]) -> "EmbedRequest":
        return cls(model=model, input=list(texts))


@dataclass
class EmbedResponse:
    model: str = ""
    embeddings: List[List[float]] = field(default_factory=list)

    @classmethod
    def from_json(cls, data: JsonDict) -> "EmbedResponse":
        return cls(model=data.get("model", ""), embeddings=data.get("embeddings") or [])


@dataclass
class EmbeddingsRequest:
    """``POST /api/embeddings`` — the legacy single-input endpoint. Prefer :class:`EmbedRequest`."""

    model: str
    prompt: str

    def to_json(self) -> JsonDict:
        return {"model": self.model, "prompt": self.prompt}


@dataclass
class EmbeddingsResponse:
    embedding: List[float] = field(default_factory=list)

    @classmethod
    def from_json(cls, data: JsonDict) -> "EmbeddingsResponse":
        return cls(embedding=data.get("embedding") or [])


@dataclass
class ModelInfo:
    name: str
    digest: Optional[str] = None
    size: Optional[int] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "ModelInfo":
        return cls(
            name=data.get("name", ""), digest=data.get("digest"), size=data.get("size")
        )


@dataclass
class TagsResponse:
    models: List[ModelInfo] = field(default_factory=list)

    @classmethod
    def from_json(cls, data: JsonDict) -> "TagsResponse":
        return cls(models=[ModelInfo.from_json(m) for m in data.get("models") or []])


_KNOWN_STATUS_FIELDS = {
    "coordinatorVersion",
    "nowUtc",
    "uptimeSeconds",
    "nodes",
    "models",
    "metrics",
    "vector",
}


@dataclass
class StatusResponse:
    """``GET /api/status`` on a coordinator. See :mod:`inferhub_client.probe` for the solo-node shape
    (added in a later phase) and how a caller tells the two apart."""

    coordinator_version: Optional[str] = None
    now_utc: Optional[str] = None
    uptime_seconds: Optional[float] = None
    nodes: Optional[List[JsonDict]] = None
    models: Optional[List[ModelInfo]] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "StatusResponse":
        models = data.get("models")
        return cls(
            coordinator_version=data.get("coordinatorVersion"),
            now_utc=data.get("nowUtc"),
            uptime_seconds=data.get("uptimeSeconds"),
            nodes=data.get("nodes"),
            models=[ModelInfo.from_json(m) for m in models]
            if models is not None
            else None,
            extra={k: v for k, v in data.items() if k not in _KNOWN_STATUS_FIELDS},
        )


# ---------------------------------------------------------------------------
# Phase 17 — retrieval: the vector data-plane, RAG headers, ingestion and search.
# ---------------------------------------------------------------------------


@dataclass
class RetrievalOptions:
    """Carried on a ``chat``/``generate`` call, never on the request dataclass itself (D1): builds
    the ``X-InferHub-Retrieve*``/``X-InferHub-Rerank`` headers via
    :func:`inferhub_client._base.build_retrieval_headers`."""

    collection: str
    k: Optional[int] = None
    model: Optional[str] = None
    mode: Optional[RetrievalMode] = None
    rerank: Optional[bool] = None


@dataclass
class VectorUpsert:
    """``POST /api/vector/{collection}/upsert``. Exactly one of ``vector``/``text`` is set — the
    hub embeds ``text`` itself when a vector is not supplied."""

    id: str
    vector: Optional[List[float]] = None
    text: Optional[str] = None
    payload: Optional[JsonDict] = None

    @classmethod
    def from_vector(
        cls, id: str, vector: List[float], payload: Optional[JsonDict] = None
    ) -> "VectorUpsert":
        return cls(id=id, vector=vector, payload=payload)

    @classmethod
    def from_text(
        cls, id: str, text: str, payload: Optional[JsonDict] = None
    ) -> "VectorUpsert":
        return cls(id=id, text=text, payload=payload)

    def to_json(self) -> JsonDict:
        body: JsonDict = {"id": self.id}
        if self.vector is not None:
            body["vector"] = self.vector
        if self.text is not None:
            body["text"] = self.text
        if self.payload is not None:
            body["payload"] = self.payload
        return body


@dataclass
class VectorQuery:
    """``POST /api/vector/{collection}/query`` (or ``/retrieve`` — same shape, RAG-oriented
    route). Exactly one of ``vector``/``text`` is set, same rule as :class:`VectorUpsert`."""

    vector: Optional[List[float]] = None
    text: Optional[str] = None
    top_k: int = 10
    filter: Optional[JsonDict] = None

    @classmethod
    def from_vector(cls, vector: List[float], top_k: int = 10) -> "VectorQuery":
        return cls(vector=vector, top_k=top_k)

    @classmethod
    def from_text(cls, text: str, top_k: int = 10) -> "VectorQuery":
        return cls(text=text, top_k=top_k)

    def with_filter(self, filter: JsonDict) -> "VectorQuery":
        self.filter = filter
        return self

    def to_json(self) -> JsonDict:
        body: JsonDict = {"topK": self.top_k}
        if self.vector is not None:
            body["vector"] = self.vector
        if self.text is not None:
            body["text"] = self.text
        if self.filter is not None:
            body["filter"] = self.filter
        return body


@dataclass
class VectorMatch:
    id: str = ""
    score: float = 0.0
    payload: Optional[JsonDict] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "VectorMatch":
        return cls(
            id=data.get("id", ""),
            score=data.get("score", 0.0),
            payload=data.get("payload"),
        )


@dataclass
class VectorRecord:
    id: str = ""
    vector: Optional[List[float]] = None
    payload: Optional[JsonDict] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "VectorRecord":
        return cls(
            id=data.get("id", ""),
            vector=data.get("vector"),
            payload=data.get("payload"),
        )


@dataclass
class TextDocument:
    """``POST /api/collections/{collection}/documents`` — a document supplied as text, not a file."""

    id: str
    text: str
    metadata: Optional[JsonDict] = None

    def to_json(self) -> JsonDict:
        body: JsonDict = {"id": self.id, "text": self.text}
        if self.metadata is not None:
            body["metadata"] = self.metadata
        return body


@dataclass
class FileDocument:
    """A document supplied as a file. ``stream`` is opened and closed by the caller — this client
    never copies file content into memory or holds it past the request (phase-16 D3: no client
    holds conversation content; the same rule extended to corpus content in phase 17)."""

    id: str
    filename: str
    stream: Any
    content_type: str = "application/octet-stream"
    metadata: Optional[JsonDict] = None


_KNOWN_INGEST_RESULT_FIELDS = {
    "documentId",
    "collection",
    "status",
    "chunks",
    "chunksEmbedded",
    "bytes",
    "contentHash",
    "error",
}


@dataclass
class IngestResult:
    """The hub's own answer to an ingest call — ``ingested``, ``unchanged`` or ``partial``.
    ``partial`` arrives as an HTTP 500 **with this exact body**, and this client returns it rather
    than raising (conformance case ``partial-ingest-is-a-500-with-a-body-not-thrown``): the
    document id and the chunks that did land are real, and a generic 5xx-is-an-exception mapping
    would throw them away."""

    document_id: str = ""
    collection: str = ""
    status: str = ""
    chunks: int = 0
    chunks_embedded: int = 0
    bytes: int = 0
    content_hash: Optional[str] = None
    error: Optional[str] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "IngestResult":
        return cls(
            document_id=data.get("documentId", ""),
            collection=data.get("collection", ""),
            status=data.get("status", ""),
            chunks=data.get("chunks", 0),
            chunks_embedded=data.get("chunksEmbedded", 0),
            bytes=data.get("bytes", 0),
            content_hash=data.get("contentHash"),
            error=data.get("error"),
            extra={
                k: v for k, v in data.items() if k not in _KNOWN_INGEST_RESULT_FIELDS
            },
        )

    @staticmethod
    def looks_like_one(data: JsonDict) -> bool:
        """A body has this shape iff it carries both ``documentId`` and ``status`` — used to tell
        an ``IngestResult`` (even on a 500) apart from a genuine error envelope (``{"error": ...}``
        with neither field), so ``_corpus.py`` knows when a non-2xx is still data."""

        return "documentId" in data and "status" in data


@dataclass
class DocumentSummary:
    document_id: str = ""
    collection: str = ""
    status: str = ""
    chunks: int = 0
    bytes: int = 0
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "DocumentSummary":
        known = {"documentId", "collection", "status", "chunks", "bytes"}
        return cls(
            document_id=data.get("documentId", ""),
            collection=data.get("collection", ""),
            status=data.get("status", ""),
            chunks=data.get("chunks", 0),
            bytes=data.get("bytes", 0),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class DocumentChunk:
    """``index`` is a **string**, not an int — the hub's chunk metadata is a string map
    (conformance case ``chunk-index-is-a-string-not-an-int``); ``page``, when present, is a real
    ``int`` on the same response, which is exactly the asymmetry the case exists to catch."""

    id: str = ""
    index: str = ""
    page: Optional[int] = None
    text: str = ""

    @classmethod
    def from_json(cls, data: JsonDict) -> "DocumentChunk":
        return cls(
            id=data.get("id", ""),
            index=data.get("index", ""),
            page=data.get("page"),
            text=data.get("text", ""),
        )


@dataclass
class DocumentChunksResponse:
    collection: str = ""
    document_id: str = ""
    chunks: List[DocumentChunk] = field(default_factory=list)

    @classmethod
    def from_json(cls, data: JsonDict) -> "DocumentChunksResponse":
        return cls(
            collection=data.get("collection", ""),
            document_id=data.get("documentId", ""),
            chunks=[DocumentChunk.from_json(c) for c in data.get("chunks") or []],
        )


@dataclass
class DocumentDeletion:
    document_id: str = ""
    deleted: bool = False

    @classmethod
    def from_json(cls, data: JsonDict) -> "DocumentDeletion":
        return cls(
            document_id=data.get("documentId", ""),
            deleted=bool(data.get("deleted", False)),
        )


@dataclass
class SearchRequest:
    """``POST /api/collections/{collection}/search``. ``mode``/``rerank`` are body fields here —
    unlike chat/generate, search takes them in the request rather than as headers (there is no
    call that both searches and does something else to overload a header onto)."""

    query: str
    top_k: int = 10
    mode: Optional[RetrievalMode] = None
    rerank: Optional[bool] = None
    filter: Optional[JsonDict] = None

    def to_json(self) -> JsonDict:
        body: JsonDict = {"query": self.query, "topK": self.top_k}
        if self.mode is not None:
            body["mode"] = self.mode
        if self.rerank is not None:
            body["rerank"] = self.rerank
        if self.filter is not None:
            body["filter"] = self.filter
        return body


@dataclass
class SearchHit:
    id: str = ""
    score: float = 0.0
    document_id: str = ""
    text: str = ""

    @classmethod
    def from_json(cls, data: JsonDict) -> "SearchHit":
        return cls(
            id=data.get("id", ""),
            score=data.get("score", 0.0),
            document_id=data.get("documentId", ""),
            text=data.get("text", ""),
        )


@dataclass
class SearchResponse:
    """``hits`` is kept in the hub's own wire order, never re-sorted by score: a reranked result
    routinely has a lower score above a higher one, and sorting "to be tidy" undoes the rerank a
    caller paid for (conformance case ``reranked-search-order-contradicts-its-own-scores``)."""

    collection: str = ""
    mode: str = ""
    hits: List[SearchHit] = field(default_factory=list)

    @classmethod
    def from_json(cls, data: JsonDict) -> "SearchResponse":
        return cls(
            collection=data.get("collection", ""),
            mode=data.get("mode", ""),
            hits=[SearchHit.from_json(h) for h in data.get("hits") or []],
        )
