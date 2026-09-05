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
