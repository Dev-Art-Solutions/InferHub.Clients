"""Text jobs — ``/api/jobs`` (hub v3.103+). A chat or a generate submitted as a job: the hub answers
at once with an id, runs it the way ``/api/chat`` would, and keeps a status the caller, the node
running it and the hub all update. For an answer that takes minutes — a big model, a model split
across nodes, a full fleet — where one held connection is the wrong shape.

A separate module mixed into both clients, like ``_corpus``: its own models, its own two-status
result (``200`` done, ``202`` not yet) and an SSE status stream.

**Hub only.** A solo node has no ``/api/jobs``; a submit there raises the 404.
"""

from __future__ import annotations

import asyncio
import time
from dataclasses import dataclass, field
from typing import Any, AsyncIterator, Dict, Iterator, List, Optional

import httpx

from ._base import parse_sse_lines, raise_for_status, read_served_by
from ._exceptions import InferHubError
from ._models import ChatRequest, ChatResponse, GenerateRequest, GenerateResponse

JsonDict = Dict[str, Any]

_TERMINAL = ("succeeded", "failed", "cancelled")


@dataclass
class JobNode:
    """The node a job was sent to. ``reported_at`` is when that node last said it is working on
    the job — it reports every couple of seconds, so a stale value means the node went quiet,
    not the model. ``None`` from a node older than v3.103."""

    id: str = ""
    name: Optional[str] = None
    reported_at: Optional[str] = None
    pieces: Optional[int] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "JobNode":
        return cls(
            id=data.get("id", ""),
            name=data.get("name"),
            reported_at=data.get("reportedAt"),
            pieces=data.get("pieces"),
        )


@dataclass
class JobGroupMember:
    """One member of a split model's group: the layers it holds and how much of its share of the
    model the head has sent it (``sent_mib`` of ``expect_mib``) — the load, while a group starts."""

    node: str = ""
    layers: Optional[int] = None
    expect_mib: Optional[int] = None
    sent_mib: Optional[int] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "JobGroupMember":
        return cls(
            node=data.get("node", ""),
            layers=data.get("layers"),
            expect_mib=data.get("expectMiB"),
            sent_mib=data.get("sentMiB"),
        )


@dataclass
class JobGroup:
    """Present when the job's model is one model split across several nodes."""

    name: str = ""
    head: Optional[str] = None
    ready: bool = False
    state: Optional[str] = None
    why: Optional[str] = None
    members: List[JobGroupMember] = field(default_factory=list)

    @classmethod
    def from_json(cls, data: JsonDict) -> "JobGroup":
        return cls(
            name=data.get("name", ""),
            head=data.get("head"),
            ready=bool(data.get("ready")),
            state=data.get("state"),
            why=data.get("why"),
            members=[JobGroupMember.from_json(m) for m in data.get("members") or []],
        )

    @property
    def loaded_fraction(self) -> Optional[float]:
        """How much of the members' shares has been sent, 0–1, while the group is coming up.
        ``None`` once the group runs (a member that kept its layers from an earlier load is sent
        only a part of its share, so the number means nothing then) and when the group does not
        say (its head is older than v3.102). For the same reason it can stop short of 1."""
        if self.state == "running":
            return None
        expect = sum(m.expect_mib or 0 for m in self.members)
        if expect <= 0:
            return None
        sent = sum(min(m.sent_mib or 0, m.expect_mib or 0) for m in self.members)
        return sent / expect


@dataclass
class JobError:
    """Why a job failed: the HTTP status the blocking route would have answered, a code
    (``refused``, ``node_error``, ``node_lost``, ``deadline``, ``cancelled``) and the sentence."""

    status: Optional[int] = None
    code: Optional[str] = None
    message: Optional[str] = None
    retry_after_seconds: Optional[int] = None

    @classmethod
    def from_json(cls, data: JsonDict) -> "JobError":
        return cls(
            status=data.get("status"),
            code=data.get("code"),
            message=data.get("message"),
            retry_after_seconds=data.get("retryAfterSeconds"),
        )


@dataclass
class Job:
    """A job's status: names, counts and times — never the request or the answer.

    ``status`` is ``queued``, ``running``, ``succeeded``, ``failed`` or ``cancelled``. ``stage``
    is ``waiting`` (at the hub; ``detail`` says for what), ``sent``, ``preparing`` (the node has
    it and its engine has produced nothing yet), ``generating`` or ``done``. ``pieces`` counts the
    pieces of the answer received so far; ``tokens`` is exact and arrives with the last one."""

    id: str = ""
    kind: str = ""
    model: str = ""
    status: str = ""
    stage: str = ""
    detail: Optional[str] = None
    created_at: Optional[str] = None
    started_at: Optional[str] = None
    updated_at: Optional[str] = None
    completed_at: Optional[str] = None
    node: Optional[JobNode] = None
    served_by: Optional[str] = None
    attempts: int = 0
    pieces: int = 0
    tokens: Optional[int] = None
    tokens_per_second: Optional[float] = None
    group: Optional[JobGroup] = None
    error: Optional[JobError] = None
    client: Optional[str] = None
    extra: JsonDict = field(default_factory=dict)

    @classmethod
    def from_json(cls, data: JsonDict) -> "Job":
        node = data.get("node")
        group = data.get("group")
        error = data.get("error")
        return cls(
            id=data.get("id", ""),
            kind=data.get("kind", ""),
            model=data.get("model", ""),
            status=data.get("status", ""),
            stage=data.get("stage", ""),
            detail=data.get("detail"),
            created_at=data.get("createdAt"),
            started_at=data.get("startedAt"),
            updated_at=data.get("updatedAt"),
            completed_at=data.get("completedAt"),
            node=JobNode.from_json(node) if node else None,
            served_by=data.get("servedBy"),
            attempts=data.get("attempts") or 0,
            pieces=data.get("pieces") or 0,
            tokens=data.get("tokens"),
            tokens_per_second=data.get("tokensPerSecond"),
            group=JobGroup.from_json(group) if group else None,
            error=JobError.from_json(error) if error else None,
            client=data.get("client"),
            extra={k: v for k, v in data.items() if k not in _KNOWN_JOB_FIELDS},
        )

    @property
    def is_terminal(self) -> bool:
        return self.status in _TERMINAL


_KNOWN_JOB_FIELDS = {
    "id",
    "kind",
    "model",
    "status",
    "stage",
    "detail",
    "createdAt",
    "startedAt",
    "updatedAt",
    "completedAt",
    "node",
    "servedBy",
    "attempts",
    "pieces",
    "tokens",
    "tokensPerSecond",
    "group",
    "error",
    "client",
}


@dataclass
class JobResult:
    """A job's answer. ``done`` is ``False`` while the job still runs — ``chat``/``generate`` then
    hold the text so far, which is what a caller that reconnects reads. Exactly one of ``chat``
    and ``generate`` is set, by the job's kind."""

    done: bool = False
    chat: Optional[ChatResponse] = None
    generate: Optional[GenerateResponse] = None
    served_by: Optional[str] = None
    raw: JsonDict = field(default_factory=dict)

    @property
    def text(self) -> str:
        """The answer's text, whichever kind the job is."""
        if self.chat is not None and self.chat.message is not None:
            return self.chat.message.content or ""
        if self.generate is not None:
            return self.generate.response or ""
        return ""


def _job_body(kind: str, request: Any) -> JsonDict:
    return {"kind": kind, "request": request.to_json()}


def _job_or_none(response: httpx.Response) -> Optional[Job]:
    if response.status_code == 404:
        return None
    raise_for_status(response)
    return Job.from_json(response.json())


def _cancelled(response: httpx.Response) -> Optional[Job]:
    # 202 + the status: a running job was asked to stop. 204: a finished job was dropped.
    if response.status_code in (204, 404):
        return None
    raise_for_status(response)
    return Job.from_json(response.json())


def _result(response: httpx.Response) -> JobResult:
    # A failed job answers its own error status and a cancelled one a 409: both raise, with the
    # hub's sentence, exactly as the blocking route's failure would have.
    raise_for_status(response)
    data = response.json()
    result = JobResult(
        done=response.status_code == 200,
        served_by=read_served_by(response),
        raw=data,
    )
    if "message" in data:
        result.chat = ChatResponse.from_json(data)
        result.chat.served_by = result.served_by
    else:
        result.generate = GenerateResponse.from_json(data)
        result.generate.served_by = result.served_by
    return result


def _gone(job_id: str) -> InferHubError:
    return InferHubError(
        404,
        f"job '{job_id}' not found — it finished and its retention passed, the hub restarted, "
        "or it was never this client's",
        "",
    )


class _JobMethodsMixin:
    """Sync job methods. Mixed into :class:`InferHubClient`; expects ``self._http``."""

    _http: httpx.Client

    def submit_chat_job(self, request: ChatRequest) -> Job:
        """``POST /api/jobs`` with a chat. Returns at once (``202``) with the job's status."""
        response = self._http.post("api/jobs", json=_job_body("chat", request))
        raise_for_status(response)
        return Job.from_json(response.json())

    def submit_generate_job(self, request: GenerateRequest) -> Job:
        """``POST /api/jobs`` with a generate."""
        response = self._http.post("api/jobs", json=_job_body("generate", request))
        raise_for_status(response)
        return Job.from_json(response.json())

    def get_job(self, job_id: str) -> Optional[Job]:
        """``GET /api/jobs/{id}``. ``None`` for an id the hub does not hold for this client."""
        return _job_or_none(self._http.get(f"api/jobs/{job_id}"))

    def list_jobs(self) -> List[Job]:
        """``GET /api/jobs`` — this client's jobs, newest first."""
        response = self._http.get("api/jobs")
        raise_for_status(response)
        return [Job.from_json(j) for j in response.json().get("jobs") or []]

    def get_job_result(self, job_id: str) -> JobResult:
        """``GET /api/jobs/{id}/result``. ``done=False`` with the text so far while the job runs;
        raises the job's own error for a failed job and a 409 for a cancelled one."""
        return _result(self._http.get(f"api/jobs/{job_id}/result"))

    def cancel_job(self, job_id: str) -> Optional[Job]:
        """``DELETE /api/jobs/{id}``. A running job is cancelled (the node is told to stop) and
        its status returned; a finished one is dropped now and ``None`` returned."""
        return _cancelled(self._http.delete(f"api/jobs/{job_id}"))

    def watch_job(self, job_id: str) -> Iterator[Job]:
        """``GET /api/jobs/{id}/events`` — yields the status each time it changes and ends after
        the terminal one. Server-sent events; use :meth:`wait_for_job` where a proxy buffers them."""
        with self._http.stream(
            "GET", f"api/jobs/{job_id}/events", timeout=None
        ) as response:
            if response.status_code >= 400:
                response.read()
            raise_for_status(response)
            for _, data in parse_sse_lines(response.iter_lines()):
                job = Job.from_json(data)
                yield job
                if job.is_terminal:
                    return

    def wait_for_job(
        self,
        job_id: str,
        *,
        poll_interval: float = 1.0,
        timeout: Optional[float] = None,
    ) -> Job:
        """Polls the status until the job is finished and returns it. Raises ``TimeoutError`` after
        ``timeout`` seconds (the job keeps running), and :class:`InferHubError` (404) if the hub
        no longer holds the job."""
        deadline = None if timeout is None else time.monotonic() + timeout
        while True:
            job = self.get_job(job_id)
            if job is None:
                raise _gone(job_id)
            if job.is_terminal:
                return job
            if deadline is not None and time.monotonic() >= deadline:
                raise TimeoutError(
                    f"job '{job_id}' is still {job.status} after {timeout} s"
                )
            time.sleep(poll_interval)


class _AsyncJobMethodsMixin:
    """Async twin of :class:`_JobMethodsMixin`."""

    _http: httpx.AsyncClient

    async def submit_chat_job(self, request: ChatRequest) -> Job:
        response = await self._http.post("api/jobs", json=_job_body("chat", request))
        raise_for_status(response)
        return Job.from_json(response.json())

    async def submit_generate_job(self, request: GenerateRequest) -> Job:
        response = await self._http.post(
            "api/jobs", json=_job_body("generate", request)
        )
        raise_for_status(response)
        return Job.from_json(response.json())

    async def get_job(self, job_id: str) -> Optional[Job]:
        return _job_or_none(await self._http.get(f"api/jobs/{job_id}"))

    async def list_jobs(self) -> List[Job]:
        response = await self._http.get("api/jobs")
        raise_for_status(response)
        return [Job.from_json(j) for j in response.json().get("jobs") or []]

    async def get_job_result(self, job_id: str) -> JobResult:
        return _result(await self._http.get(f"api/jobs/{job_id}/result"))

    async def cancel_job(self, job_id: str) -> Optional[Job]:
        return _cancelled(await self._http.delete(f"api/jobs/{job_id}"))

    async def watch_job(self, job_id: str) -> AsyncIterator[Job]:
        async with self._http.stream(
            "GET", f"api/jobs/{job_id}/events", timeout=None
        ) as response:
            if response.status_code >= 400:
                await response.aread()
            raise_for_status(response)
            lines: List[str] = []
            async for line in response.aiter_lines():
                lines.append(line)
                if line != "":
                    continue
                for _, data in parse_sse_lines(iter(lines)):
                    job = Job.from_json(data)
                    yield job
                    if job.is_terminal:
                        return
                lines = []

    async def wait_for_job(
        self,
        job_id: str,
        *,
        poll_interval: float = 1.0,
        timeout: Optional[float] = None,
    ) -> Job:
        deadline = None if timeout is None else time.monotonic() + timeout
        while True:
            job = await self.get_job(job_id)
            if job is None:
                raise _gone(job_id)
            if job.is_terminal:
                return job
            if deadline is not None and time.monotonic() >= deadline:
                raise TimeoutError(
                    f"job '{job_id}' is still {job.status} after {timeout} s"
                )
            await asyncio.sleep(poll_interval)
