"""Phase 18 — audio (``/v1/audio/*``) and images (``/v1/images/*`` + ``/api/images/jobs``). A
separate module for the same reason ``_corpus.py`` is (phase 17 D3): two modalities with
multipart upload, read-once content and their own SSE framing would otherwise leave ``_client.py``
covering four unrelated planes. Mixed into both :class:`~inferhub_client._client.InferHubClient`
and :class:`~inferhub_client._async_client.AsyncInferHubClient`, same split as ``_corpus.py``.
"""

from __future__ import annotations

import base64
from typing import AsyncIterator, Iterator, Optional

import httpx

from ._base import parse_sse_lines, raise_for_status
from ._exceptions import InferHubOpenAiException
from ._models import (
    ImageContent,
    ImageEditRequest,
    ImageGenerationRequest,
    ImageResponse,
    ImageVariationRequest,
    MediaJob,
    MediaJobList,
    SpeechAudio,
    SpeechChunk,
    SpeechRequest,
    Transcription,
    TranscriptionDocument,
    TranscriptionRequest,
)


def _speech_audio_from_response(response: httpx.Response) -> SpeechAudio:
    return SpeechAudio(
        response=response,
        content_type=response.headers.get("Content-Type"),
        sample_rate=_int_header(response, "X-InferHub-Audio-Sample-Rate"),
        characters=_int_header(response, "X-InferHub-Speech-Characters"),
        served_by=response.headers.get("X-InferHub-Served-By"),
    )


def _int_header(response: httpx.Response, name: str) -> Optional[int]:
    raw = response.headers.get(name)
    if raw is None:
        return None
    try:
        return int(raw)
    except ValueError:
        return None


def _image_edit_form(request: ImageEditRequest) -> tuple:
    # Options travel as X-InferHub-Image-* headers (see _image_headers), never as form fields.
    data = {"model": request.model, "prompt": request.prompt}
    files = {
        "image": (request.image_filename, request.image, request.image_content_type)
    }
    if request.mask is not None:
        files["mask"] = (
            request.mask_filename or "mask.png",
            request.mask,
            request.mask_content_type,
        )
    return data, files


def _image_variation_form(request: ImageVariationRequest) -> tuple:
    data = {"model": request.model}
    files = {
        "image": (request.image_filename, request.image, request.image_content_type)
    }
    return data, files


def _image_headers(options) -> dict:
    return options.to_headers() if options is not None else {}


def _job_submit_data(request: ImageGenerationRequest, operation: str) -> dict:
    body = request.to_json()
    body["operation"] = operation
    return body


class _MediaMethodsMixin:
    """Sync audio + images. Mixed into :class:`InferHubClient`; expects ``self._http`` to be an
    ``httpx.Client``."""

    _http: httpx.Client

    # -- Audio ----------------------------------------------------------------------------------

    def transcribe(self, request: TranscriptionRequest) -> Transcription:
        """``POST /v1/audio/transcriptions`` — always asks for ``verbose_json`` regardless of
        ``request.response_format`` (dotnet D6)."""
        data = request.to_form_fields(response_format="verbose_json")
        files = {"file": (request.filename, request.audio, request.content_type)}
        response = self._http.post("v1/audio/transcriptions", data=data, files=files)
        raise_for_status(response)
        return Transcription.from_json(response.json())

    def transcribe_document(
        self, request: TranscriptionRequest
    ) -> TranscriptionDocument:
        """``POST /v1/audio/transcriptions`` rendered as ``text``/``srt``/``vtt`` and returned
        unaltered."""
        data = request.to_form_fields()
        files = {"file": (request.filename, request.audio, request.content_type)}
        response = self._http.post("v1/audio/transcriptions", data=data, files=files)
        raise_for_status(response)
        return TranscriptionDocument(
            content=response.content,
            content_type=response.headers.get("Content-Type", "text/plain"),
            served_by=response.headers.get("X-InferHub-Served-By"),
        )

    def create_speech(self, request: SpeechRequest) -> SpeechAudio:
        """``POST /v1/audio/speech`` — hands back the live response stream whether or not
        ``stream_format`` is set (dotnet D2, load-bearing: not one byte of caller code differs).
        The caller owns and must close ``result.response``."""
        req = self._http.build_request(
            "POST", "v1/audio/speech", json=request.to_json()
        )
        response = self._http.send(req, stream=True)
        if not response.is_success:
            response.read()
            raise_for_status(response)
        return _speech_audio_from_response(response)

    def stream_speech(self, request: SpeechRequest) -> Iterator[SpeechChunk]:
        """``POST /v1/audio/speech`` with ``stream_format`` forced to ``"sse"`` — yields one
        :class:`SpeechChunk` per ``speech.audio.delta``/``speech.audio.done`` frame. A
        ``speech.audio.error`` frame raises :class:`~inferhub_client.InferHubOpenAiException`."""
        request.stream_format = "sse"
        with self._http.stream(
            "POST", "v1/audio/speech", json=request.to_json()
        ) as response:
            raise_for_status(response)
            served_by = response.headers.get("X-InferHub-Served-By")
            sample_rate = _int_header(response, "X-InferHub-Audio-Sample-Rate")
            characters = _int_header(response, "X-InferHub-Speech-Characters")
            for event, data in parse_sse_lines(response.iter_lines()):
                if event == "speech.audio.error":
                    error = data.get("error", data)
                    message = (
                        error.get("message", str(error))
                        if isinstance(error, dict)
                        else str(error)
                    )
                    raise InferHubOpenAiException(
                        response.status_code,
                        message,
                        str(data),
                        error_code=error.get("code")
                        if isinstance(error, dict)
                        else None,
                    )
                audio_b64 = data.get("audio")
                chunk = SpeechChunk(
                    type=event or data.get("type", ""),
                    audio=base64.b64decode(audio_b64)
                    if isinstance(audio_b64, str)
                    else None,
                    usage=data.get("usage"),
                    characters=characters,
                    served_by=served_by,
                    sample_rate=sample_rate,
                    extra={
                        k: v
                        for k, v in data.items()
                        if k not in ("audio", "usage", "type")
                    },
                )
                yield chunk
                if chunk.type == "speech.audio.done":
                    return

    # -- Images: synchronous OpenAI routes -------------------------------------------------------

    def generate_image(self, request: ImageGenerationRequest) -> ImageResponse:
        """``POST /v1/images/generations``."""
        response = self._http.post(
            "v1/images/generations",
            json=request.to_json(),
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return ImageResponse.from_json(response.json())

    def edit_image(self, request: ImageEditRequest) -> ImageResponse:
        """``POST /v1/images/edits``, multipart."""
        data, files = _image_edit_form(request)
        response = self._http.post(
            "v1/images/edits",
            data=data,
            files=files,
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return ImageResponse.from_json(response.json())

    def create_image_variation(self, request: ImageVariationRequest) -> ImageResponse:
        """``POST /v1/images/variations``, multipart."""
        data, files = _image_variation_form(request)
        response = self._http.post(
            "v1/images/variations",
            data=data,
            files=files,
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return ImageResponse.from_json(response.json())

    # -- Images: the async job seam --------------------------------------------------------------

    def submit_image_generation(self, request: ImageGenerationRequest) -> MediaJob:
        """``POST /api/images/jobs``, JSON — queues a generation and returns immediately."""
        response = self._http.post(
            "api/images/jobs",
            json=_job_submit_data(request, "generate"),
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return MediaJob.from_json(response.json())

    def submit_image_edit(self, request: ImageEditRequest) -> MediaJob:
        """``POST /api/images/jobs``, multipart with ``operation=edit``."""
        data, files = _image_edit_form(request)
        data["operation"] = "edit"
        response = self._http.post(
            "api/images/jobs",
            data=data,
            files=files,
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return MediaJob.from_json(response.json())

    def submit_image_variation(self, request: ImageVariationRequest) -> MediaJob:
        """``POST /api/images/jobs``, multipart with ``operation=variation``."""
        data, files = _image_variation_form(request)
        data["operation"] = "variation"
        response = self._http.post(
            "api/images/jobs",
            data=data,
            files=files,
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return MediaJob.from_json(response.json())

    def list_image_jobs(self) -> MediaJobList:
        """``GET /api/images/jobs`` — this client's jobs, oldest first, client-scoped."""
        response = self._http.get("api/images/jobs")
        raise_for_status(response)
        return MediaJobList.from_json(response.json())

    def get_image_job(self, job_id: str) -> Optional[MediaJob]:
        """``GET /api/images/jobs/{id}`` — ``None`` on 404 (not yours, or not there — the same
        body either way)."""
        response = self._http.get(f"api/images/jobs/{job_id}")
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return MediaJob.from_json(response.json())

    def watch_image_job(self, job_id: str) -> Iterator[MediaJob]:
        """``GET /api/images/jobs/{id}/events`` — one :class:`MediaJob` per SSE frame. Walking
        away does not cancel the job."""
        with self._http.stream("GET", f"api/images/jobs/{job_id}/events") as response:
            raise_for_status(response)
            for _event, data in parse_sse_lines(response.iter_lines()):
                job = MediaJob.from_json(data)
                yield job
                if job.state in ("succeeded", "failed", "cancelled", "expired"):
                    return

    def open_image_content(self, job_id: str, index: int) -> ImageContent:
        """``GET /api/images/jobs/{id}/content/{index}`` — **read once**. The caller owns and
        must close ``result.response``. ``410 job_expired`` on a repeat read, ``409
        job_not_ready``, ``404 image_not_found``."""
        req = self._http.build_request(
            "GET", f"api/images/jobs/{job_id}/content/{index}"
        )
        response = self._http.send(req, stream=True)
        if not response.is_success:
            response.read()
            raise_for_status(response)
        return ImageContent(
            response=response,
            content_type=response.headers.get("Content-Type"),
            projection=response.headers.get("X-InferHub-Image-Projection"),
            seam_repair=response.headers.get("X-InferHub-Image-Seam-Repair"),
        )

    def cancel_image_job(self, job_id: str) -> MediaJob:
        """``DELETE /api/images/jobs/{id}`` — best effort; the returned job says what actually
        happened. A job already terminal is a ``409 job_terminal``."""
        response = self._http.delete(f"api/images/jobs/{job_id}")
        raise_for_status(response)
        return MediaJob.from_json(response.json())


class _AsyncMediaMethodsMixin:
    """Async twin of :class:`_MediaMethodsMixin`. Mixed into :class:`AsyncInferHubClient`;
    expects ``self._http`` to be an ``httpx.AsyncClient``."""

    _http: httpx.AsyncClient

    async def transcribe(self, request: TranscriptionRequest) -> Transcription:
        data = request.to_form_fields(response_format="verbose_json")
        files = {"file": (request.filename, request.audio, request.content_type)}
        response = await self._http.post(
            "v1/audio/transcriptions", data=data, files=files
        )
        raise_for_status(response)
        return Transcription.from_json(response.json())

    async def transcribe_document(
        self, request: TranscriptionRequest
    ) -> TranscriptionDocument:
        data = request.to_form_fields()
        files = {"file": (request.filename, request.audio, request.content_type)}
        response = await self._http.post(
            "v1/audio/transcriptions", data=data, files=files
        )
        raise_for_status(response)
        return TranscriptionDocument(
            content=response.content,
            content_type=response.headers.get("Content-Type", "text/plain"),
            served_by=response.headers.get("X-InferHub-Served-By"),
        )

    async def create_speech(self, request: SpeechRequest) -> SpeechAudio:
        req = self._http.build_request(
            "POST", "v1/audio/speech", json=request.to_json()
        )
        response = await self._http.send(req, stream=True)
        if not response.is_success:
            await response.aread()
            raise_for_status(response)
        return _speech_audio_from_response(response)

    async def stream_speech(self, request: SpeechRequest) -> AsyncIterator[SpeechChunk]:
        """``POST /v1/audio/speech`` with ``stream_format`` forced to ``"sse"``. Async twin of
        the sync generator — ``parse_sse_lines`` takes a plain iterator, so the accumulation loop
        is inlined here rather than adapted over ``aiter_lines()`` (mirrors :meth:`watch_image_job`)."""
        request.stream_format = "sse"
        async with self._http.stream(
            "POST", "v1/audio/speech", json=request.to_json()
        ) as response:
            raise_for_status(response)
            served_by = response.headers.get("X-InferHub-Served-By")
            sample_rate = _int_header(response, "X-InferHub-Audio-Sample-Rate")
            characters = _int_header(response, "X-InferHub-Speech-Characters")
            event: Optional[str] = None
            data_lines: list = []

            async def _emit(evt, raw_lines):
                import json as _json

                raw = "\n".join(raw_lines)
                try:
                    data = _json.loads(raw)
                except (_json.JSONDecodeError, ValueError):
                    data = {"raw": raw}
                if evt == "speech.audio.error":
                    error = data.get("error", data)
                    message = (
                        error.get("message", str(error))
                        if isinstance(error, dict)
                        else str(error)
                    )
                    raise InferHubOpenAiException(
                        response.status_code,
                        message,
                        str(data),
                        error_code=error.get("code")
                        if isinstance(error, dict)
                        else None,
                    )
                audio_b64 = data.get("audio")
                return SpeechChunk(
                    type=evt or data.get("type", ""),
                    audio=base64.b64decode(audio_b64)
                    if isinstance(audio_b64, str)
                    else None,
                    usage=data.get("usage"),
                    characters=characters,
                    served_by=served_by,
                    sample_rate=sample_rate,
                    extra={
                        k: v
                        for k, v in data.items()
                        if k not in ("audio", "usage", "type")
                    },
                )

            async for line in response.aiter_lines():
                if line == "":
                    if data_lines:
                        chunk = await _emit(event, data_lines)
                        yield chunk
                        if chunk.type == "speech.audio.done":
                            return
                    event, data_lines = None, []
                    continue
                if line.startswith(":"):
                    continue
                if line.startswith("event:"):
                    event = line[len("event:") :].strip()
                elif line.startswith("data:"):
                    data_lines.append(line[len("data:") :].strip())

    # -- Images: synchronous OpenAI routes -------------------------------------------------------

    async def generate_image(self, request: ImageGenerationRequest) -> ImageResponse:
        response = await self._http.post(
            "v1/images/generations",
            json=request.to_json(),
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return ImageResponse.from_json(response.json())

    async def edit_image(self, request: ImageEditRequest) -> ImageResponse:
        data, files = _image_edit_form(request)
        response = await self._http.post(
            "v1/images/edits",
            data=data,
            files=files,
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return ImageResponse.from_json(response.json())

    async def create_image_variation(
        self, request: ImageVariationRequest
    ) -> ImageResponse:
        data, files = _image_variation_form(request)
        response = await self._http.post(
            "v1/images/variations",
            data=data,
            files=files,
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return ImageResponse.from_json(response.json())

    # -- Images: the async job seam --------------------------------------------------------------

    async def submit_image_generation(
        self, request: ImageGenerationRequest
    ) -> MediaJob:
        response = await self._http.post(
            "api/images/jobs",
            json=_job_submit_data(request, "generate"),
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return MediaJob.from_json(response.json())

    async def submit_image_edit(self, request: ImageEditRequest) -> MediaJob:
        data, files = _image_edit_form(request)
        data["operation"] = "edit"
        response = await self._http.post(
            "api/images/jobs",
            data=data,
            files=files,
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return MediaJob.from_json(response.json())

    async def submit_image_variation(self, request: ImageVariationRequest) -> MediaJob:
        data, files = _image_variation_form(request)
        data["operation"] = "variation"
        response = await self._http.post(
            "api/images/jobs",
            data=data,
            files=files,
            headers=_image_headers(request.options),
        )
        raise_for_status(response)
        return MediaJob.from_json(response.json())

    async def list_image_jobs(self) -> MediaJobList:
        response = await self._http.get("api/images/jobs")
        raise_for_status(response)
        return MediaJobList.from_json(response.json())

    async def get_image_job(self, job_id: str) -> Optional[MediaJob]:
        response = await self._http.get(f"api/images/jobs/{job_id}")
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return MediaJob.from_json(response.json())

    async def watch_image_job(self, job_id: str) -> AsyncIterator[MediaJob]:
        async with self._http.stream(
            "GET", f"api/images/jobs/{job_id}/events"
        ) as response:
            raise_for_status(response)
            data_lines: list = []
            async for line in response.aiter_lines():
                if line == "":
                    if data_lines:
                        import json as _json

                        raw = "\n".join(data_lines)
                        try:
                            data = _json.loads(raw)
                        except (_json.JSONDecodeError, ValueError):
                            data = {"raw": raw}
                        job = MediaJob.from_json(data)
                        yield job
                        if job.state in ("succeeded", "failed", "cancelled", "expired"):
                            return
                    data_lines = []
                    continue
                if line.startswith(":"):
                    continue
                if line.startswith("event:"):
                    continue
                if line.startswith("data:"):
                    data_lines.append(line[len("data:") :].strip())

    async def open_image_content(self, job_id: str, index: int) -> ImageContent:
        req = self._http.build_request(
            "GET", f"api/images/jobs/{job_id}/content/{index}"
        )
        response = await self._http.send(req, stream=True)
        if not response.is_success:
            await response.aread()
            raise_for_status(response)
        return ImageContent(
            response=response,
            content_type=response.headers.get("Content-Type"),
            projection=response.headers.get("X-InferHub-Image-Projection"),
            seam_repair=response.headers.get("X-InferHub-Image-Seam-Repair"),
        )

    async def cancel_image_job(self, job_id: str) -> MediaJob:
        response = await self._http.delete(f"api/images/jobs/{job_id}")
        raise_for_status(response)
        return MediaJob.from_json(response.json())
