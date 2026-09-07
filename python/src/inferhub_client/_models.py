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


# ---------------------------------------------------------------------------
# Phase 18 — audio: POST /v1/audio/transcriptions, POST /v1/audio/speech.
#
# No InferHubCallOptions-equivalent here, deliberately (dotnet phase-9 D7): neither route reads
# X-InferHub-Provider, X-InferHub-Conversation or the retrieval headers — audio dispatches to a
# node that declared the capability, no cloud provider is in the path.
# ---------------------------------------------------------------------------


@dataclass
class TranscriptionRequest:
    """``POST /v1/audio/transcriptions``, multipart. ``audio`` is a caller-owned, already-open
    file-like object — this client never closes it (same rule as :class:`FileDocument`)."""

    model: str
    audio: Any
    filename: str
    content_type: str = "application/octet-stream"
    language: Optional[str] = None
    prompt: Optional[str] = None
    temperature: Optional[float] = None
    response_format: Optional[str] = None

    def to_form_fields(self, *, response_format: Optional[str] = None) -> JsonDict:
        """Every field before the file part, always (dotnet phase-9 D5): above
        ``Tools:MaxStreamedBytes`` the hub routes from the leading fields and streams the bytes
        past them, so a field written after ``file`` is a ``400`` on a large upload and silently
        fine on a small one — the bug that only appears in production."""
        fields: JsonDict = {"model": self.model}
        if self.language is not None:
            fields["language"] = self.language
        if self.prompt is not None:
            fields["prompt"] = self.prompt
        if self.temperature is not None:
            fields["temperature"] = str(self.temperature)
        fields["response_format"] = response_format or self.response_format or "json"
        return fields


@dataclass
class TranscriptionSegment:
    id: Optional[int] = None
    start: Optional[float] = None
    end: Optional[float] = None
    text: str = ""
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "TranscriptionSegment":
        known = {"id", "start", "end", "text"}
        return cls(
            id=data.get("id"),
            start=data.get("start"),
            end=data.get("end"),
            text=data.get("text", ""),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class Transcription:
    """``TranscribeAsync``'s answer — always requested as ``verbose_json`` regardless of what the
    caller asked for (dotnet D6), because these are the fields a caller does something with. For
    ``text``/``srt``/``vtt`` use :func:`InferHubClient.transcribe_document`."""

    text: str = ""
    language: Optional[str] = None
    duration: Optional[float] = None
    segments: List[TranscriptionSegment] = field(default_factory=list)
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "Transcription":
        known = {"text", "language", "duration", "segments"}
        return cls(
            text=data.get("text", ""),
            language=data.get("language"),
            duration=data.get("duration"),
            segments=[
                TranscriptionSegment.from_json(s) for s in data.get("segments") or []
            ],
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class TranscriptionDocument:
    """The hub's ``text``/``srt``/``vtt`` bytes, returned **unaltered** — the only honest thing to
    do with a subtitle file. ``content`` is buffered (unlike :class:`SpeechAudio`/
    :class:`ImageContent`): the hub renders these from a worker's segments at request time rather
    than unlinking a stored object, so there is no unlink-on-read hazard to preserve a stream for."""

    content: bytes
    content_type: str
    served_by: Optional[str] = None

    @property
    def text(self) -> str:
        return self.content.decode("utf-8")


@dataclass
class SpeechRequest:
    """``POST /v1/audio/speech``. ``stream_format`` is ``None`` for the whole file at once,
    ``"audio"`` for a framed binary stream, or ``"sse"`` (forced by :func:`stream_speech`, never
    set here directly) — only ``wav``/``pcm`` can stream; anything else is a ``400`` from the hub
    before a node is chosen."""

    model: str
    input: str
    voice: Optional[str] = None
    response_format: Optional[str] = None
    stream_format: Optional[str] = None

    def to_json(self) -> JsonDict:
        body: JsonDict = {"model": self.model, "input": self.input}
        if self.voice is not None:
            body["voice"] = self.voice
        if self.response_format is not None:
            body["response_format"] = self.response_format
        if self.stream_format is not None:
            body["stream_format"] = self.stream_format
        return body


@dataclass
class SpeechAudio:
    """``CreateSpeechAsync``'s answer — the live response, read-once by nature of being an HTTP
    stream (root rule 7): the caller owns ``response`` and is responsible for closing it (a
    context manager, ``with speech.response:``, or an explicit ``.close()``/``.aclose()``).
    ``sample_rate``/``characters`` are ``None`` unless the hub measured and stamped one (streaming
    synthesis only, from ``X-InferHub-Audio-Sample-Rate``/``X-InferHub-Speech-Characters``) — a
    missing value, never a guessed one."""

    response: Any
    content_type: Optional[str] = None
    sample_rate: Optional[int] = None
    characters: Optional[int] = None
    served_by: Optional[str] = None


@dataclass
class SpeechChunk:
    """One SSE frame of :func:`stream_speech` — a ``speech.audio.delta`` carrying ``audio``, or
    the terminal ``speech.audio.done`` carrying ``usage`` and no audio. ``audio`` is decoded from
    the frame's base64. The terminal frame is yielded like any other rather than swallowed
    (dotnet D3): **a usage of three zeros is a true count** for a phoneme model that tokenized
    nothing, and the number that reconciles with a bill is ``characters`` — read once from
    ``X-InferHub-Speech-Characters`` and stamped on every chunk beside ``served_by``, not a body
    field."""

    type: str = ""
    audio: Optional[bytes] = None
    usage: Optional[JsonDict] = None
    characters: Optional[int] = None
    served_by: Optional[str] = None
    sample_rate: Optional[int] = None
    extra: JsonDict = field(default_factory=dict)


# ---------------------------------------------------------------------------
# Phase 18 — images: the synchronous /v1/images/* routes and the async job seam,
# /api/images/jobs. Same OpenAI envelope and same InferHubOpenAiException on both
# (mirrors dotnet's IInferHubImagesClient remarks).
# ---------------------------------------------------------------------------


@dataclass
class ImageOptions:
    """The ``X-InferHub-Image-*`` extension headers — not body fields on the hub. Numbers are
    formatted with plain ``str()``; Python has no locale-dependent decimal separator surprise the
    way ``CultureInfo`` does in .NET, so there is no invariant-culture concern to carry here
    (dotnet D4's problem does not exist in this runtime)."""

    steps: Optional[int] = None
    guidance: Optional[float] = None
    seed: Optional[int] = None
    strength: Optional[float] = None
    mask_convention: Optional[str] = None
    seam_repair: Optional[str] = None
    projection: Optional[str] = None

    def to_headers(self) -> JsonDict:
        headers: JsonDict = {}
        if self.steps is not None:
            headers["X-InferHub-Image-Steps"] = str(self.steps)
        if self.guidance is not None:
            headers["X-InferHub-Image-Guidance"] = str(self.guidance)
        if self.seed is not None:
            headers["X-InferHub-Image-Seed"] = str(self.seed)
        if self.strength is not None:
            headers["X-InferHub-Image-Strength"] = str(self.strength)
        if self.mask_convention is not None:
            headers["X-InferHub-Image-Mask-Convention"] = self.mask_convention
        if self.seam_repair is not None:
            headers["X-InferHub-Image-Seam-Repair"] = self.seam_repair
        if self.projection is not None:
            headers["X-InferHub-Image-Projection"] = self.projection
        return headers


@dataclass
class ImageGenerationRequest:
    model: str
    prompt: str
    negative_prompt: Optional[str] = None
    n: Optional[int] = None
    size: Optional[str] = None
    seed: Optional[int] = None
    response_format: Optional[str] = None
    options: Optional[ImageOptions] = None

    def to_json(self) -> JsonDict:
        body: JsonDict = {"model": self.model, "prompt": self.prompt}
        if self.negative_prompt is not None:
            body["negative_prompt"] = self.negative_prompt
        if self.n is not None:
            body["n"] = self.n
        if self.size is not None:
            body["size"] = self.size
        if self.seed is not None:
            body["seed"] = self.seed
        if self.response_format is not None:
            body["response_format"] = self.response_format
        return body


@dataclass
class ImageEditRequest:
    """A picture and a prompt, multipart. With a mask, only the masked area is redrawn; without
    one, this is image-to-image. Two request types, not one with an ``operation`` field (dotnet
    D5): the hub's two refusals — ``"a variation takes no prompt"``, ``"a variation takes no
    mask"`` — are then unrepresentable in this client's types instead of merely disallowed."""

    model: str
    image: Any
    image_filename: str
    prompt: str
    mask: Optional[Any] = None
    mask_filename: Optional[str] = None
    image_content_type: str = "application/octet-stream"
    mask_content_type: str = "application/octet-stream"
    options: Optional[ImageOptions] = None


@dataclass
class ImageVariationRequest:
    """No prompt, no mask: see :class:`ImageEditRequest`'s remarks."""

    model: str
    image: Any
    image_filename: str
    image_content_type: str = "application/octet-stream"
    options: Optional[ImageOptions] = None


@dataclass
class ImageData:
    b64_json: Optional[str] = None
    size: Optional[str] = None
    seed: Optional[int] = None
    projection: Optional[str] = None
    seam_delta: Optional[float] = None
    seam_repair: Optional[str] = None
    seam_delta_before: Optional[float] = None
    revised_prompt: Optional[str] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "ImageData":
        known = {
            "b64_json",
            "size",
            "seed",
            "projection",
            "seam_delta",
            "seam_repair",
            "seam_delta_before",
            "revised_prompt",
        }
        return cls(
            b64_json=data.get("b64_json"),
            size=data.get("size"),
            seed=data.get("seed"),
            projection=data.get("projection"),
            seam_delta=data.get("seam_delta"),
            seam_repair=data.get("seam_repair"),
            seam_delta_before=data.get("seam_delta_before"),
            revised_prompt=data.get("revised_prompt"),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class ImageResponse:
    """``POST /v1/images/generations|edits|variations``'s answer — pictures come back base64 in
    the envelope, because the hub stores nothing and so has no URL to serve. For a render that
    should outlive one HTTP connection, submit it as a job instead."""

    created: Optional[int] = None
    data: List[ImageData] = field(default_factory=list)
    prompt_augmented: Optional[str] = None
    trigger: Optional[str] = None
    warnings: Optional[List[str]] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "ImageResponse":
        known = {"created", "data", "prompt_augmented", "trigger", "warnings"}
        return cls(
            created=data.get("created"),
            data=[ImageData.from_json(d) for d in data.get("data") or []],
            prompt_augmented=data.get("prompt_augmented"),
            trigger=data.get("trigger"),
            warnings=data.get("warnings"),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class MediaJobOutput:
    index: Optional[int] = None
    url: Optional[str] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "MediaJobOutput":
        known = {"index", "url"}
        return cls(
            index=data.get("index"),
            url=data.get("url"),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class MediaJob:
    """The one job document both images and video jobs render through (dotnet D2 — the hub's
    ``ImageJobView.Describe`` serves both capabilities from one type, ``capability`` telling them
    apart), reused here rather than typed twice."""

    id: str = ""
    state: str = ""
    capability: str = ""
    step: Optional[int] = None
    total_steps: Optional[int] = None
    images: List[MediaJobOutput] = field(default_factory=list)
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "MediaJob":
        known = {"id", "state", "capability", "step", "totalSteps", "images"}
        return cls(
            id=data.get("id", ""),
            state=data.get("state", ""),
            capability=data.get("capability", ""),
            step=data.get("step"),
            total_steps=data.get("totalSteps"),
            images=[MediaJobOutput.from_json(i) for i in data.get("images") or []],
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class MediaJobList:
    """``GET /api/images/jobs`` — client-scoped, never fleet-wide: holding a job id is how a
    picture is fetched, so listing other tenants' ids would be handing them out. **Lists work, not
    results** — a job whose images were already delivered is still here with nothing left to
    fetch."""

    jobs: List[MediaJob] = field(default_factory=list)
    queued: int = 0
    active: int = 0
    retained_bytes: int = 0
    retention_seconds: int = 0
    persistence: str = ""

    @classmethod
    def from_json(cls, data: JsonDict) -> "MediaJobList":
        return cls(
            jobs=[MediaJob.from_json(j) for j in data.get("jobs") or []],
            queued=data.get("queued", 0),
            active=data.get("active", 0),
            retained_bytes=data.get("retainedBytes", 0),
            retention_seconds=data.get("retentionSeconds", 0),
            persistence=data.get("persistence", ""),
        )


@dataclass
class ImageContent:
    """``GET /api/images/jobs/{id}/content/{index}`` — **read once**: the hub unlinks the bytes as
    they are read, so a retry is a ``410``. The caller owns ``response`` (root rule 7); this
    client never buffers it."""

    response: Any
    content_type: Optional[str] = None
    projection: Optional[str] = None
    seam_repair: Optional[str] = None


# ---------------------------------------------------------------------------
# Phase 18 — admin: /api/admin/*, an admin key, and the vector-collection lifecycle. Plain
# {"error": "..."} envelope (never the OpenAI one), so admin failures surface as InferHubError,
# same as the core surface — no new exception type earns its keep here.
#
# Selectors/model catalogues/retrieval-profile bodies stay as passthrough JsonDict (`extra`-style)
# rather than a fully typed hierarchy: this client does not validate them client-side (dotnet's
# non-goal carries over unchanged — the hub is the authority on what a selector means), so nothing
# is lost by not naming every nested field, and the alternative is a dozen near-empty dataclasses
# that exist only to be re-serialized unchanged.
# ---------------------------------------------------------------------------


@dataclass
class AdminNode:
    node_id: str = ""
    name: Optional[str] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "AdminNode":
        known = {"nodeId", "name"}
        return cls(
            node_id=data.get("nodeId", ""),
            name=data.get("name"),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class CollectionInfo:
    name: str = ""
    dimension: int = 0
    distance: Optional[str] = None
    record_count: Optional[int] = None
    operations: Optional[int] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "CollectionInfo":
        known = {"name", "dimension", "distance", "recordCount", "operations"}
        return cls(
            name=data.get("name", ""),
            dimension=data.get("dimension", 0),
            distance=data.get("distance"),
            record_count=data.get("recordCount"),
            operations=data.get("operations"),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class CollectionsResponse:
    collections: List[CollectionInfo] = field(default_factory=list)
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "CollectionsResponse":
        return cls(
            collections=[
                CollectionInfo.from_json(c) for c in data.get("collections") or []
            ],
            extra={k: v for k, v in data.items() if k != "collections"},
        )


@dataclass
class CollectionDetail:
    name: str = ""
    dimension: int = 0
    distance: Optional[str] = None
    under_replicated: Optional[bool] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "CollectionDetail":
        known = {"name", "dimension", "distance", "underReplicated"}
        return cls(
            name=data.get("name", ""),
            dimension=data.get("dimension", 0),
            distance=data.get("distance"),
            under_replicated=data.get("underReplicated"),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class NodeProfile:
    """One record for both directions (dotnet D6) — ``name``/``revision`` are ignored on write,
    the hub sets both from the route and its own counter regardless of what is sent."""

    name: str = ""
    revision: int = 0
    selector: JsonDict = field(default_factory=dict)
    models: Optional[JsonDict] = None
    max_concurrency: Optional[int] = None
    retrieval: Optional[JsonDict] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "NodeProfile":
        known = {
            "name",
            "revision",
            "selector",
            "models",
            "maxConcurrency",
            "retrieval",
        }
        return cls(
            name=data.get("name", ""),
            revision=data.get("revision", 0),
            selector=data.get("selector") or {},
            models=data.get("models"),
            max_concurrency=data.get("maxConcurrency"),
            retrieval=data.get("retrieval"),
            extra={k: v for k, v in data.items() if k not in known},
        )

    def to_json(self) -> JsonDict:
        body: JsonDict = {"selector": self.selector}
        if self.models is not None:
            body["models"] = self.models
        if self.max_concurrency is not None:
            body["maxConcurrency"] = self.max_concurrency
        if self.retrieval is not None:
            body["retrieval"] = self.retrieval
        return body


@dataclass
class PutProfileResult:
    profile: Optional[NodeProfile] = None
    applied: List[str] = field(default_factory=list)
    conflicts: List[str] = field(default_factory=list)

    @classmethod
    def from_json(cls, data: JsonDict) -> "PutProfileResult":
        profile = data.get("profile")
        return cls(
            profile=NodeProfile.from_json(profile) if profile is not None else None,
            applied=data.get("applied") or [],
            conflicts=data.get("conflicts") or [],
        )


@dataclass
class DeleteProfileResult:
    reasserted: List[str] = field(default_factory=list)
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "DeleteProfileResult":
        known = {"reasserted"}
        return cls(
            reasserted=data.get("reasserted") or [],
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class NodeProfileState:
    node_id: str = ""
    desired: Optional[JsonDict] = None
    effective: Optional[JsonDict] = None
    refusals: List[JsonDict] = field(default_factory=list)
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "NodeProfileState":
        known = {"nodeId", "desired", "effective", "refusals"}
        return cls(
            node_id=data.get("nodeId", ""),
            desired=data.get("desired"),
            effective=data.get("effective"),
            refusals=data.get("refusals") or [],
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class ModelCommandAccepted:
    """The literal ``202`` body a pull/delete/warm command answers with. ``reused`` means somebody
    already asked — surfaced, not hidden: a caller polling for their own command id needs to know
    it may be watching someone else's."""

    node_id: str = ""
    model: str = ""
    kind: str = ""
    command_id: str = ""
    reused: bool = False
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "ModelCommandAccepted":
        known = {"nodeId", "model", "kind", "commandId", "reused"}
        return cls(
            node_id=data.get("nodeId", ""),
            model=data.get("model", ""),
            kind=data.get("kind", ""),
            command_id=data.get("commandId", ""),
            reused=bool(data.get("reused", False)),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class FleetModelMatrix:
    """``GET /api/admin/models`` — kept as a thin wrapper over the wire dict rather than a typed
    grid: which nodes hold each model is exactly the shape the hub sends and a caller reads it the
    same way it arrived."""

    models: List[JsonDict] = field(default_factory=list)
    nodes: List[JsonDict] = field(default_factory=list)
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "FleetModelMatrix":
        known = {"models", "nodes"}
        return cls(
            models=data.get("models") or [],
            nodes=data.get("nodes") or [],
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class EnsureModelResult:
    """``POST /api/admin/models/{model}/ensure`` — the hub's full placement reasoning, not just a
    boolean (dotnet D3): ``decision`` is what an operator escalates on."""

    satisfied: bool = False
    decision: JsonDict = field(default_factory=dict)
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "EnsureModelResult":
        known = {"satisfied", "decision"}
        return cls(
            satisfied=bool(data.get("satisfied", False)),
            decision=data.get("decision") or {},
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class UsageRow:
    """The hub's actual ``GET /api/admin/usage`` projection (dotnet D4) — counts only, never a
    prompt or a completion (hub 25 D3): the ledger could not carry one even if asked."""

    client_id: str = ""
    model: str = ""
    requests: int = 0
    prompt_tokens: int = 0
    completion_tokens: int = 0
    total_tokens: int = 0
    fallback_requests: int = 0

    @classmethod
    def from_json(cls, data: JsonDict) -> "UsageRow":
        return cls(
            client_id=data.get("clientId", ""),
            model=data.get("model", ""),
            requests=data.get("requests", 0),
            prompt_tokens=data.get("promptTokens", 0),
            completion_tokens=data.get("completionTokens", 0),
            total_tokens=data.get("totalTokens", 0),
            fallback_requests=data.get("fallbackRequests", 0),
        )


@dataclass
class UsageResponse:
    rows: List[UsageRow] = field(default_factory=list)

    @classmethod
    def from_json(cls, data: JsonDict) -> "UsageResponse":
        rows = data.get("rows") if isinstance(data, dict) else data
        return cls(rows=[UsageRow.from_json(r) for r in rows or []])


@dataclass
class ClientRow:
    """Never a key, by construction (dotnet D5) — ``ClientConfig.Key`` never leaves the hub
    process, so there is no field here for a caller to notice is always empty."""

    client_id: str = ""
    limits: Optional[JsonDict] = None
    live_usage: Optional[JsonDict] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "ClientRow":
        known = {"clientId", "limits", "liveUsage"}
        return cls(
            client_id=data.get("clientId", ""),
            limits=data.get("limits"),
            live_usage=data.get("liveUsage"),
            extra={k: v for k, v in data.items() if k not in known},
        )


@dataclass
class AdminEvent:
    """One frame of ``GET /api/admin/stream`` — ``event`` is the SSE event name (``snapshot``,
    ``vector.*``, ``model-progress``, ...), ``data`` its parsed JSON payload. No reconnect variant
    is offered here (see ``python/README.md``): this client's contract is one HTTP connection, one
    generator, ending when the server closes the stream or the caller stops iterating."""

    event: Optional[str] = None
    data: JsonDict = field(default_factory=dict)


# ---------------------------------------------------------------------------
# Phase 18 — the node: a base address, not a second client (root rule 6 / roadmap D7).
# ---------------------------------------------------------------------------


@dataclass
class NodeBackendInfo:
    name: Optional[str] = None
    endpoint: Optional[str] = None
    health: Optional[str] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "NodeBackendInfo":
        return cls(
            name=data.get("name"),
            endpoint=data.get("endpoint"),
            health=data.get("health"),
        )


@dataclass
class NodeConcurrency:
    limit: int = 0
    in_flight: int = 0

    @classmethod
    def from_json(cls, data: JsonDict) -> "NodeConcurrency":
        return cls(limit=data.get("limit", 0), in_flight=data.get("inFlight", 0))


@dataclass
class NodeGpuInfo:
    cuda: bool = False
    devices: int = 0
    names: List[str] = field(default_factory=list)

    @classmethod
    def from_json(cls, data: JsonDict) -> "NodeGpuInfo":
        return cls(
            cuda=bool(data.get("cuda", False)),
            devices=data.get("devices", 0),
            names=data.get("names") or [],
        )


@dataclass
class NodeRetrievalInfo:
    """``rerank`` is a **string** (``"none"``/``"llm"``, the config-level rerank mode), never a
    bool — the conformance corpus's founding case (``node-status-rerank-is-a-string``): dotnet
    typed it ``bool?`` in v1.7.0 and threw a ``JsonException`` the first time it was driven
    against a real node with retrieval on. This client is typed correctly from the start because
    the case exists before the bug had a chance to happen here."""

    enabled: bool = False
    provider: Optional[str] = None
    embedding_model: Optional[str] = None
    mode: Optional[str] = None
    rerank: Optional[str] = None
    collections: List[JsonDict] = field(default_factory=list)
    error: Optional[str] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "NodeRetrievalInfo":
        return cls(
            enabled=bool(data.get("enabled", False)),
            provider=data.get("provider"),
            embedding_model=data.get("embeddingModel"),
            mode=data.get("mode"),
            rerank=data.get("rerank"),
            collections=data.get("collections") or [],
            error=data.get("error"),
        )


@dataclass
class NodeStatusResponse:
    """A solo node's ``GET /api/status`` — deliberately a smaller, different document than
    :class:`StatusResponse`. ``mode`` is always ``"solo"`` and is the only field that tells the
    two documents apart; there is no fleet array, no queue block, no replica count, because a node
    with no coordinator has no concept of any of them."""

    mode: str = "solo"
    node_version: Optional[str] = None
    now_utc: Optional[str] = None
    name: Optional[str] = None
    backend: Optional[NodeBackendInfo] = None
    concurrency: Optional[NodeConcurrency] = None
    gpu: Optional[NodeGpuInfo] = None
    capabilities: List[str] = field(default_factory=list)
    retrieval: Optional[NodeRetrievalInfo] = None
    models: List[ModelInfo] = field(default_factory=list)
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "NodeStatusResponse":
        known = {
            "mode",
            "nodeVersion",
            "nowUtc",
            "name",
            "backend",
            "concurrency",
            "gpu",
            "capabilities",
            "retrieval",
            "models",
        }
        backend = data.get("backend")
        concurrency = data.get("concurrency")
        gpu = data.get("gpu")
        retrieval = data.get("retrieval")
        return cls(
            mode=data.get("mode", "solo"),
            node_version=data.get("nodeVersion"),
            now_utc=data.get("nowUtc"),
            name=data.get("name"),
            backend=NodeBackendInfo.from_json(backend) if backend is not None else None,
            concurrency=NodeConcurrency.from_json(concurrency)
            if concurrency is not None
            else None,
            gpu=NodeGpuInfo.from_json(gpu) if gpu is not None else None,
            capabilities=data.get("capabilities") or [],
            retrieval=NodeRetrievalInfo.from_json(retrieval)
            if retrieval is not None
            else None,
            models=[ModelInfo.from_json(m) for m in data.get("models") or []],
            extra={k: v for k, v in data.items() if k not in known},
        )


#: ``"hub"`` | ``"solo_node"`` — :attr:`InferHubTargetProbe.kind`.
InferHubTargetKind = str


@dataclass
class InferHubTargetProbe:
    """``probe()``'s answer — one ``GET /api/status``, discriminated on whether the body carries
    ``mode`` (present → a solo node; the hub's document never has the field at all). Exactly one
    of ``hub_status``/``node_status`` is set, matching ``kind``."""

    kind: InferHubTargetKind
    version: Optional[str] = None
    hub_status: Optional[StatusResponse] = None
    node_status: Optional[NodeStatusResponse] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "InferHubTargetProbe":
        if "mode" in data:
            node_status = NodeStatusResponse.from_json(data)
            return cls(
                kind="solo_node",
                version=node_status.node_version,
                node_status=node_status,
            )
        hub_status = StatusResponse.from_json(data)
        return cls(
            kind="hub", version=hub_status.coordinator_version, hub_status=hub_status
        )


class VideoErrorCodes:
    """The hub's ``501 not_supported`` refusals on ``GET /v1/videos`` and
    ``POST /v1/videos/{id}/remix`` (root rule 10) — taught here rather than published as methods
    that can only throw. There is no ``video`` module in this client: an id is itself the
    capability to fetch the bytes, and nothing durable holds the prompt that made a clip, so
    neither a listing nor a remix can ever be served. See ``python/README.md``'s Video section for
    the recorded refusal bodies and the alternative (send a new request with the prompt you want)."""

    NOT_SUPPORTED = "not_supported"
