"""Phase 18 — audio: transcription (parsed and verbatim) and speech (buffered and streamed)."""

from __future__ import annotations

import io
import json

import httpx
import pytest

from inferhub_client import (
    InferHubOpenAiException,
    SpeechRequest,
    TranscriptionRequest,
)

from .conftest import RecordingTransport


def make_client(status_code, body, **kwargs):
    transport = RecordingTransport(status_code, body, **kwargs)
    http = httpx.Client(base_url="http://localhost:5080/", transport=transport)
    from inferhub_client import InferHubClient

    return InferHubClient(http_client=http), transport


def test_transcribe_forces_verbose_json_and_parses_segments():
    body = json.dumps(
        {
            "text": "hello world",
            "language": "en",
            "duration": 1.2,
            "segments": [{"id": 0, "start": 0.0, "end": 1.2, "text": "hello world"}],
        }
    )
    client, transport = make_client(200, body)
    request = TranscriptionRequest(
        model="whisper",
        audio=io.BytesIO(b"fake"),
        filename="a.wav",
        response_format="text",
    )
    result = client.transcribe(request)
    assert result.text == "hello world"
    assert result.segments[0].text == "hello world"

    sent = transport.requests[0]
    assert b'name="response_format"' in sent.content
    assert b"verbose_json" in sent.content
    # The file part is last (dotnet D5 — every field before the file, always).
    assert sent.content.rindex(b'name="file"') > sent.content.rindex(b'name="model"')


def test_transcribe_document_returns_bytes_verbatim():
    client, _ = make_client(
        200,
        "1\n00:00:00,000 --> 00:00:01,000\nhello\n\n",
        headers={},
        media_type="text/plain",
    )
    request = TranscriptionRequest(
        model="whisper",
        audio=io.BytesIO(b"fake"),
        filename="a.wav",
        response_format="srt",
    )
    result = client.transcribe_document(request)
    assert "hello" in result.text
    assert (
        result.content_type == "text/plain; charset=utf-8"
        or "text/plain" in result.content_type
    )


def test_create_speech_returns_the_response_with_headers_stamped():
    client, _ = make_client(
        200,
        b"RIFF....WAVEfmt ",
        headers={
            "X-InferHub-Served-By": "node-1",
            "X-InferHub-Audio-Sample-Rate": "22050",
        },
        media_type="audio/wav",
    )
    result = client.create_speech(
        SpeechRequest(model="piper", input="hi", response_format="wav")
    )
    assert result.sample_rate == 22050
    assert result.served_by == "node-1"
    result.response.close()


def test_stream_speech_yields_delta_then_terminal_zero_usage_frame():
    import base64

    audio_b64 = base64.b64encode(b"\x00\x01").decode()
    body = (
        f'event: speech.audio.delta\ndata: {{"type":"speech.audio.delta","audio":"{audio_b64}"}}\n\n'
        'event: speech.audio.done\ndata: {"type":"speech.audio.done","usage":'
        '{"input_tokens":0,"output_tokens":0,"total_tokens":0}}\n\n'
    )
    client, _ = make_client(
        200,
        body,
        headers={
            "X-InferHub-Served-By": "node-1",
            "X-InferHub-Speech-Characters": "2",
        },
        media_type="text/event-stream",
    )
    chunks = list(
        client.stream_speech(
            SpeechRequest(model="piper", input="hi", response_format="wav")
        )
    )
    assert len(chunks) == 2
    assert chunks[0].audio == b"\x00\x01"
    assert chunks[0].characters == 2
    # The terminal frame's three zeros are a true count, not a placeholder (dotnet D3).
    assert chunks[1].type == "speech.audio.done"
    assert chunks[1].usage == {"input_tokens": 0, "output_tokens": 0, "total_tokens": 0}
    assert chunks[1].audio is None


def test_stream_speech_raises_on_error_frame():
    body = (
        'event: speech.audio.error\ndata: {"error":{"message":"node dropped",'
        '"code":"capability_unavailable"}}\n\n'
    )
    client, _ = make_client(200, body, media_type="text/event-stream")
    with pytest.raises(InferHubOpenAiException) as excinfo:
        list(client.stream_speech(SpeechRequest(model="piper", input="hi")))
    assert excinfo.value.error_code == "capability_unavailable"


def test_audio_speech_capability_unavailable_carries_retry_after():
    body = (
        '{"error":{"message":"no node currently provides \'speak\' for model \'gemma:2b\'",'
        '"type":"api_error","param":null,"code":"capability_unavailable"}}'
    )
    client, _ = make_client(503, body, headers={"Retry-After": "30"})
    with pytest.raises(InferHubOpenAiException) as excinfo:
        client.create_speech(SpeechRequest(model="gemma:2b", input="hi"))
    assert excinfo.value.status_code == 503
    assert excinfo.value.error_code == "capability_unavailable"
    assert excinfo.value.retry_after == 30.0
