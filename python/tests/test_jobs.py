"""Text jobs (hub v3.103+). The status bodies and the result headers are **recorded** from a real
coordinator and a real node over Ollama (qwen2.5:0.5b) on 2026-11-16. The ``group`` body is
**derived** from the hub's serializer (``JobEndpoints.Describe``) — no split-model group was
running when these were recorded — and so is the SSE framing."""

from __future__ import annotations

import json

import httpx
import pytest

from inferhub_client import (
    AsyncInferHubClient,
    ChatMessage,
    ChatRequest,
    GenerateRequest,
    InferHubClient,
    InferHubError,
)

from .conftest import RecordingTransport

_ID = "8755d357-7f7a-493b-984b-09eeb1c3797e"

_QUEUED = (
    '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b",'
    '"status":"queued","stage":"waiting","createdAt":"2026-11-16T11:40:16.2677184+00:00",'
    '"updatedAt":"2026-11-16T11:40:16.2677184+00:00","attempts":0,"pieces":0}'
)

_PREPARING = (
    '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b",'
    '"status":"running","stage":"preparing","createdAt":"2026-11-16T11:40:16.2677184+00:00",'
    '"startedAt":"2026-11-16T11:40:16.2950626+00:00","updatedAt":"2026-11-16T11:40:18.3304721+00:00",'
    '"node":{"id":"d2b53940-e3b4-4b37-855d-536e9a4fe10e","name":"live150-node",'
    '"reportedAt":"2026-11-16T11:40:18.3304721+00:00","pieces":0},"attempts":1,"pieces":0}'
)

_SUCCEEDED = (
    '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b",'
    '"status":"succeeded","stage":"done","createdAt":"2026-11-16T11:40:16.2677184+00:00",'
    '"startedAt":"2026-11-16T11:40:16.2950626+00:00","updatedAt":"2026-11-16T11:40:33.342876+00:00",'
    '"completedAt":"2026-11-16T11:40:33.342876+00:00","node":{"id":"d2b53940-e3b4-4b37-855d-536e9a4fe10e",'
    '"name":"live150-node","reportedAt":"2026-11-16T11:40:32.4983246+00:00","pieces":1},'
    '"attempts":1,"pieces":300,"tokens":300,"tokensPerSecond":340.23}'
)

_PARTIAL_RESULT = (
    '{"model":"qwen2.5:0.5b","message":{"role":"assistant","content":""},"done":false}'
)

_RESULT = (
    '{"model":"qwen2.5:0.5b","created_at":"2026-11-16T11:40:33.3396733Z",'
    '"message":{"role":"assistant","content":"Ah, the lighthouse keeper."},"done":true,'
    '"done_reason":"length","eval_count":300}'
)

# Derived, not recorded.
_GROUP = (
    '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"wizardlm-70b-split",'
    '"status":"running","stage":"preparing","detail":"group starting: 18420 of 25022 MiB sent to its members",'
    '"attempts":1,"pieces":0,"group":{"name":"wizardlm-70b-split","head":"shard-a","ready":false,'
    '"state":"starting","members":[{"node":"shard-a","layers":27},'
    '{"node":"shard-b","layers":27,"expectMiB":12511,"sentMiB":9210},'
    '{"node":"shard-c","layers":27,"expectMiB":12511,"sentMiB":9210}]}}'
)

_FAILED = (
    '{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"llama3","status":"failed",'
    '"stage":"done","attempts":1,"pieces":4,"error":{"status":502,"code":"node_lost",'
    '"message":"node \'gpu-1\' disappeared while this job was running; it was not retried"}}'
)


def make_client(status_code, body, **kwargs):
    transport = RecordingTransport(status_code, body, **kwargs)
    http = httpx.Client(base_url="http://localhost:5080/", transport=transport)
    return InferHubClient(http_client=http), transport


def test_submit_wraps_the_chat_request_and_returns_the_status_at_once():
    client, transport = make_client(202, _QUEUED)
    job = client.submit_chat_job(
        ChatRequest(model="qwen2.5:0.5b", messages=[ChatMessage("user", "hi")])
    )

    sent = json.loads(transport.requests[0].content)
    assert transport.requests[0].url.path == "/api/jobs"
    assert sent["kind"] == "chat"
    assert sent["request"]["model"] == "qwen2.5:0.5b"
    assert sent["request"]["messages"][0]["content"] == "hi"
    assert job.id == _ID
    assert job.status == "queued" and job.stage == "waiting"
    assert not job.is_terminal
    assert job.node is None


def test_submit_generate_names_its_kind():
    client, transport = make_client(202, _QUEUED)
    client.submit_generate_job(GenerateRequest(model="qwen2.5:0.5b", prompt="hi"))
    assert json.loads(transport.requests[0].content)["kind"] == "generate"


def test_a_running_status_carries_the_nodes_own_report():
    client, _ = make_client(200, _PREPARING)
    job = client.get_job(_ID)
    assert job.status == "running" and job.stage == "preparing"
    assert job.node.name == "live150-node"
    assert job.node.reported_at == "2026-11-16T11:40:18.3304721+00:00"
    assert job.attempts == 1


def test_a_finished_status_has_exact_tokens():
    client, _ = make_client(200, _SUCCEEDED)
    job = client.get_job(_ID)
    assert job.is_terminal and job.status == "succeeded"
    assert job.tokens == 300 and job.pieces == 300
    assert job.tokens_per_second == 340.23


def test_a_split_models_status_says_how_far_each_member_is():
    client, _ = make_client(200, _GROUP)
    job = client.get_job(_ID)
    assert job.detail.startswith("group starting")
    assert job.group.state == "starting" and not job.group.ready
    assert [m.node for m in job.group.members] == ["shard-a", "shard-b", "shard-c"]
    assert job.group.members[0].expect_mib is None
    assert job.group.members[1].sent_mib == 9210
    assert round(job.group.loaded_fraction, 3) == round(18420 / 25022, 3)


def test_an_unknown_job_is_none_not_an_error():
    client, _ = make_client(404, '{"error":"job \'x\' not found"}')
    assert client.get_job(_ID) is None


def test_the_result_of_a_running_job_is_the_text_so_far_and_not_done():
    client, _ = make_client(202, _PARTIAL_RESULT)
    result = client.get_job_result(_ID)
    assert result.done is False
    assert result.text == ""


def test_the_result_of_a_finished_job_is_the_chat_answer_with_who_served_it():
    client, transport = make_client(
        200, _RESULT, headers={"X-InferHub-Served-By": "node"}
    )
    result = client.get_job_result(_ID)
    assert transport.requests[0].url.path == f"/api/jobs/{_ID}/result"
    assert result.done is True
    assert result.text == "Ah, the lighthouse keeper."
    assert result.chat.eval_count == 300
    assert result.served_by == "node"
    assert result.generate is None


def test_the_result_of_a_failed_job_raises_its_own_status():
    client, _ = make_client(502, '{"error":"node \'gpu-1\' disappeared"}')
    with pytest.raises(InferHubError) as raised:
        client.get_job_result(_ID)
    assert raised.value.status_code == 502


def test_a_failed_status_is_read_not_raised():
    client, _ = make_client(200, _FAILED)
    job = client.get_job(_ID)
    assert job.status == "failed"
    assert job.error.code == "node_lost" and job.error.status == 502


def test_cancel_returns_the_status_and_dropping_a_finished_job_returns_none():
    client, transport = make_client(202, _FAILED.replace('"failed"', '"cancelled"'))
    assert client.cancel_job(_ID).status == "cancelled"
    assert transport.requests[0].method == "DELETE"

    client, _ = make_client(204, "")
    assert client.cancel_job(_ID) is None


def test_list_jobs_reads_the_envelope():
    client, _ = make_client(200, '{"jobs":[' + _SUCCEEDED + "," + _PREPARING + "]}")
    assert [j.status for j in client.list_jobs()] == ["succeeded", "running"]


def test_watch_yields_each_status_and_stops_at_the_terminal_one():
    stream = (
        f"event: status\ndata: {_PREPARING}\n\n: waiting\n\n"
        f"event: status\ndata: {_SUCCEEDED}\n\n"
        f"event: status\ndata: {_QUEUED}\n\n"
    )
    client, transport = make_client(200, stream, media_type="text/event-stream")
    seen = [job.status for job in client.watch_job(_ID)]
    assert seen == ["running", "succeeded"]
    assert transport.requests[0].url.path == f"/api/jobs/{_ID}/events"


def test_wait_polls_until_the_job_is_finished():
    bodies = [_QUEUED, _PREPARING, _SUCCEEDED]

    def handle(request: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200, content=bodies.pop(0) if len(bodies) > 1 else bodies[0]
        )

    http = httpx.Client(
        base_url="http://localhost:5080/", transport=httpx.MockTransport(handle)
    )
    job = InferHubClient(http_client=http).wait_for_job(_ID, poll_interval=0)
    assert job.status == "succeeded"


def test_wait_times_out_while_the_job_keeps_running_and_a_gone_job_raises():
    client, _ = make_client(200, _PREPARING)
    with pytest.raises(TimeoutError):
        client.wait_for_job(_ID, poll_interval=0, timeout=0)

    client, _ = make_client(404, '{"error":"job not found"}')
    with pytest.raises(InferHubError) as raised:
        client.wait_for_job(_ID, poll_interval=0)
    assert raised.value.status_code == 404


async def test_the_async_client_has_the_same_surface():
    stream = (
        f"event: status\ndata: {_PREPARING}\n\nevent: status\ndata: {_SUCCEEDED}\n\n"
    )

    def handle(request: httpx.Request) -> httpx.Response:
        if request.url.path.endswith("/events"):
            return httpx.Response(
                200, content=stream, headers={"content-type": "text/event-stream"}
            )
        if request.url.path.endswith("/result"):
            return httpx.Response(
                200, content=_RESULT, headers={"X-InferHub-Served-By": "node"}
            )
        if request.method == "POST":
            return httpx.Response(202, content=_QUEUED)
        return httpx.Response(200, content=_SUCCEEDED)

    http = httpx.AsyncClient(
        base_url="http://localhost:5080/", transport=httpx.MockTransport(handle)
    )
    client = AsyncInferHubClient(http_client=http)

    job = await client.submit_chat_job(
        ChatRequest(model="qwen2.5:0.5b", messages=[ChatMessage("user", "hi")])
    )
    assert job.status == "queued"
    assert [j.status async for j in client.watch_job(job.id)] == [
        "running",
        "succeeded",
    ]
    assert (await client.wait_for_job(job.id, poll_interval=0)).tokens == 300
    assert (await client.get_job_result(job.id)).text == "Ah, the lighthouse keeper."
