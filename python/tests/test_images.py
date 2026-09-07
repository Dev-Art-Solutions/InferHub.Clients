"""Phase 18 — images: the synchronous /v1/images/* routes and the /api/images/jobs seam."""

from __future__ import annotations

import io
import json

import httpx
import pytest

from inferhub_client import (
    ImageEditRequest,
    ImageGenerationRequest,
    ImageOptions,
    ImageVariationRequest,
    InferHubOpenAiException,
)

from .conftest import RecordingTransport


def make_client(status_code, body, **kwargs):
    transport = RecordingTransport(status_code, body, **kwargs)
    http = httpx.Client(base_url="http://localhost:5080/", transport=transport)
    from inferhub_client import InferHubClient

    return InferHubClient(http_client=http), transport


def test_generate_image_parses_the_envelope_and_sends_option_headers():
    body = json.dumps({"created": 1, "data": [{"b64_json": "AAA=", "seed": 42}]})
    client, transport = make_client(200, body)
    result = client.generate_image(
        ImageGenerationRequest(
            model="sdxl",
            prompt="a lighthouse in fog",
            options=ImageOptions(steps=28, seed=42),
        )
    )
    assert result.data[0].b64_json == "AAA="
    assert result.data[0].seed == 42

    sent = transport.requests[0]
    assert sent.headers["X-InferHub-Image-Steps"] == "28"
    assert sent.headers["X-InferHub-Image-Seed"] == "42"


def test_edit_image_is_multipart_with_no_operation_field():
    client, transport = make_client(200, '{"created":1,"data":[]}')
    client.edit_image(
        ImageEditRequest(
            model="sdxl",
            image=io.BytesIO(b"png"),
            image_filename="a.png",
            prompt="brighter",
        )
    )
    assert b'name="operation"' not in transport.requests[0].content
    assert b'name="prompt"' in transport.requests[0].content


def test_create_image_variation_has_no_prompt_field():
    client, transport = make_client(200, '{"created":1,"data":[]}')
    client.create_image_variation(
        ImageVariationRequest(
            model="sdxl", image=io.BytesIO(b"png"), image_filename="a.png"
        )
    )
    assert b'name="prompt"' not in transport.requests[0].content


def test_submit_image_generation_carries_operation_generate():
    client, transport = make_client(
        200, '{"id":"job-1","state":"queued","capability":"image"}'
    )
    job = client.submit_image_generation(
        ImageGenerationRequest(model="sdxl", prompt="p")
    )
    assert job.id == "job-1"
    sent = json.loads(transport.requests[0].content)
    assert sent["operation"] == "generate"


def test_submit_image_edit_and_variation_are_multipart_with_operation():
    client, transport = make_client(200, '{"id":"job-2","state":"queued"}')
    client.submit_image_edit(
        ImageEditRequest(
            model="sdxl", image=io.BytesIO(b"x"), image_filename="a.png", prompt="p"
        )
    )
    assert b'name="operation"\r\n\r\nedit' in transport.requests[0].content

    client2, transport2 = make_client(200, '{"id":"job-3","state":"queued"}')
    client2.submit_image_variation(
        ImageVariationRequest(
            model="sdxl", image=io.BytesIO(b"x"), image_filename="a.png"
        )
    )
    assert b'name="operation"\r\n\r\nvariation' in transport2.requests[0].content


def test_list_image_jobs_parses_the_recorded_empty_listing():
    # Recorded from a live 3.37.0 — dotnet phase 10's §1 example.
    body = '{"jobs":[],"queued":0,"active":0,"retainedBytes":0,"retentionSeconds":300,"persistence":"none"}'
    client, _ = make_client(200, body)
    result = client.list_image_jobs()
    assert result.jobs == []
    assert result.retention_seconds == 300
    assert result.persistence == "none"


def test_get_image_job_returns_none_on_404():
    client, _ = make_client(404, '{"error":"not found"}')
    assert client.get_image_job("nope") is None


def test_watch_image_job_stops_at_terminal_state():
    body = (
        'data: {"id":"j1","state":"running","capability":"image","step":1,"totalSteps":2}\n\n'
        'data: {"id":"j1","state":"succeeded","capability":"image","step":2,"totalSteps":2,'
        '"images":[{"index":0,"url":"/api/images/jobs/j1/content/0"}]}\n\n'
    )
    client, _ = make_client(200, body, media_type="text/event-stream")
    frames = list(client.watch_image_job("j1"))
    assert len(frames) == 2
    assert frames[-1].state == "succeeded"
    assert frames[-1].images[0].url == "/api/images/jobs/j1/content/0"


def test_open_image_content_carries_projection_headers():
    client, _ = make_client(
        200,
        b"\x89PNG",
        headers={"X-InferHub-Image-Projection": "equirectangular"},
        media_type="image/png",
    )
    content = client.open_image_content("j1", 0)
    assert content.projection == "equirectangular"
    content.response.close()


def test_open_image_content_expired_is_410_job_expired():
    body = '{"error":{"message":"this image has already been read","type":"api_error","param":null,"code":"job_expired"}}'
    client, _ = make_client(410, body)
    with pytest.raises(InferHubOpenAiException) as excinfo:
        client.open_image_content("j1", 0)
    assert excinfo.value.status_code == 410
    assert excinfo.value.error_code == "job_expired"


def test_cancel_image_job_returns_the_job_as_it_actually_ended():
    client, _ = make_client(200, '{"id":"j1","state":"succeeded","capability":"image"}')
    job = client.cancel_image_job("j1")
    # Cancelled at the last step, still succeeded — the client does not override the server's word.
    assert job.state == "succeeded"


def test_submit_image_generation_503_capability_unavailable():
    # The recorded conformance corpus case, driven directly rather than through the runner.
    body = (
        '{"error":{"message":"no node currently provides \'image\' for model \'llava:latest\'",'
        '"type":"api_error","param":null,"code":"capability_unavailable"}}'
    )
    client, _ = make_client(503, body, headers={"Retry-After": "30"})
    with pytest.raises(InferHubOpenAiException) as excinfo:
        client.submit_image_generation(
            ImageGenerationRequest(model="llava:latest", prompt="x")
        )
    assert excinfo.value.retry_after == 30.0
