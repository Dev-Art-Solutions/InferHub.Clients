import json

import httpx
import pytest

from inferhub_client import (
    AsyncInferHubClient,
    ChatMessage,
    ChatRequest,
    EmbedRequest,
    GenerateRequest,
    InferHubError,
    InferHubRetrievalException,
    RetrievalOptions,
    TextDocument,
    VectorQuery,
    VectorUpsert,
)

from .conftest import RecordingTransport


def make_client(status_code, body, **kwargs):
    transport = RecordingTransport(status_code, body, **kwargs)
    http = httpx.AsyncClient(base_url="http://localhost:5080/", transport=transport)
    return AsyncInferHubClient(http_client=http), transport


@pytest.mark.asyncio
async def test_chat_forces_stream_false_and_parses_the_message():
    client, transport = make_client(
        200,
        '{"model":"llama3","message":{"role":"assistant","content":"pong"},"done":true}',
    )
    result = await client.chat(
        ChatRequest(model="llama3", messages=[ChatMessage("user", "ping")], stream=True)
    )
    assert result.message.content == "pong"

    sent = json.loads(transport.requests[0].content)
    assert sent["stream"] is False


@pytest.mark.asyncio
async def test_chat_stream_yields_deltas_and_stops_at_done():
    body = (
        '{"model":"llama3","message":{"role":"assistant","content":"hel"},"done":false}\n'
        '{"model":"llama3","message":{"role":"assistant","content":"lo"},"done":false}\n'
        '{"model":"llama3","message":{"role":"assistant","content":"!"},"done":true}\n'
    )
    client, _ = make_client(200, body)
    deltas = []
    async for chunk in client.chat_stream(ChatRequest(model="llama3", messages=[])):
        deltas.append(chunk.message.content)
    assert deltas == ["hel", "lo", "!"]


@pytest.mark.asyncio
async def test_chat_stream_raises_on_terminal_error_chunk_with_partial_seen():
    body = (
        '{"model":"llama3","message":{"role":"assistant","content":"partial"},"done":false}\n'
        '{"error":"node dropped mid-stream","done":true}\n'
    )
    client, _ = make_client(200, body)

    seen = []
    with pytest.raises(InferHubError) as excinfo:
        async for chunk in client.chat_stream(ChatRequest(model="llama3", messages=[])):
            seen.append(chunk.message.content)

    assert str(excinfo.value) == "node dropped mid-stream"
    assert seen == ["partial"]


@pytest.mark.asyncio
async def test_generate_blocking_and_streaming():
    client, _ = make_client(200, '{"model":"llama3","response":"pong","done":true}')
    result = await client.generate(GenerateRequest(model="llama3", prompt="ping"))
    assert result.response == "pong"

    stream_client, _ = make_client(
        200,
        '{"model":"llama3","response":"a","done":false}\n{"model":"llama3","response":"b","done":true}\n',
    )
    deltas = [
        c.response
        async for c in stream_client.generate_stream(
            GenerateRequest(model="llama3", prompt="p")
        )
    ]
    assert deltas == ["a", "b"]


@pytest.mark.asyncio
async def test_embed_raises_on_empty_vector_list():
    client, _ = make_client(200, '{"model":"nomic-embed-text","embeddings":[]}')
    with pytest.raises(InferHubError):
        await client.embed(EmbedRequest.from_text("nomic-embed-text", "hi"))


@pytest.mark.asyncio
async def test_ping_true_on_2xx_false_otherwise():
    up, _ = make_client(200, "OK", media_type="text/plain")
    assert await up.ping() is True

    down, _ = make_client(503, "down", media_type="text/plain")
    assert await down.ping() is False


@pytest.mark.asyncio
async def test_404_surfaces_the_hubs_error_message():
    client, _ = make_client(404, '{"error":"model \'nope\' not found"}')
    with pytest.raises(InferHubError) as excinfo:
        await client.chat(ChatRequest(model="nope", messages=[]))
    assert excinfo.value.status_code == 404
    assert str(excinfo.value) == "model 'nope' not found"


@pytest.mark.asyncio
async def test_async_context_manager_closes_owned_client():
    async with AsyncInferHubClient("http://localhost:5080/") as client:
        assert client._http.is_closed is False
    assert client._http.is_closed is True


@pytest.mark.asyncio
async def test_async_context_manager_does_not_close_a_caller_supplied_client():
    http = httpx.AsyncClient(base_url="http://localhost:5080/")
    async with AsyncInferHubClient(http_client=http):
        pass
    assert http.is_closed is False
    await http.aclose()


# -- Phase 17: retrieval -----------------------------------------------------------------------


@pytest.mark.asyncio
async def test_chat_with_retrieval_sends_the_headers():
    client, transport = make_client(
        200,
        '{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true}',
    )
    await client.chat(
        ChatRequest(model="llama3", messages=[ChatMessage("user", "hi")]),
        retrieval=RetrievalOptions(collection="docs", k=5),
    )
    sent = transport.requests[0].headers
    assert sent["X-InferHub-Retrieve"] == "docs"
    assert sent["X-InferHub-Retrieve-K"] == "5"


@pytest.mark.asyncio
async def test_424_raises_the_retrieval_specific_exception():
    client, _ = make_client(424, '{"error":"retrieval unavailable"}')
    with pytest.raises(InferHubRetrievalException):
        await client.chat(
            ChatRequest(model="llama3", messages=[]),
            retrieval=RetrievalOptions(collection="docs"),
        )


@pytest.mark.asyncio
async def test_upsert_and_query_round_trip():
    client, _ = make_client(200, '{"id":"v1","vector":[0.1,0.2]}')
    record = await client.upsert("docs", VectorUpsert.from_vector("v1", [0.1, 0.2]))
    assert record.id == "v1"

    client2, _ = make_client(200, '{"matches":[{"id":"v1","score":0.9}]}')
    matches = await client2.query("docs", VectorQuery.from_vector([0.1, 0.2]))
    assert matches[0].id == "v1"


@pytest.mark.asyncio
async def test_ingest_text_partial_500_is_returned_not_raised():
    client, _ = make_client(
        500,
        '{"documentId":"z","collection":"handbook","status":"partial","chunks":1,'
        '"chunksEmbedded":0,"bytes":11}',
    )
    result = await client.ingest_text("handbook", TextDocument(id="z", text="x"))
    assert result.status == "partial"


@pytest.mark.asyncio
async def test_search_keeps_wire_order():
    client, _ = make_client(
        200,
        '{"collection":"handbook","mode":"hybrid","hits":['
        '{"id":"a","score":0.01,"documentId":"policy.txt","text":"..."},'
        '{"id":"b","score":0.03,"documentId":"onboarding","text":"..."}]}',
    )
    result = await client.search("handbook", "payroll")
    assert [h.document_id for h in result.hits] == ["policy.txt", "onboarding"]


# -- Phase 18 spot checks: audio, images, admin, node (full coverage lives in the sync
# test_audio.py/test_images.py/test_admin.py/test_node.py; the async mixins are thin twins of
# the sync ones over the same _base.py plumbing, so these confirm the async wiring itself). ------


@pytest.mark.asyncio
async def test_async_transcribe_forces_verbose_json():
    import io

    from inferhub_client import TranscriptionRequest

    client, transport = make_client(200, '{"text":"hi","segments":[]}')
    request = TranscriptionRequest(
        model="whisper", audio=io.BytesIO(b"fake"), filename="a.wav"
    )
    result = await client.transcribe(request)
    assert result.text == "hi"
    assert b"verbose_json" in transport.requests[0].content


@pytest.mark.asyncio
async def test_async_generate_image_parses_the_envelope():
    from inferhub_client import ImageGenerationRequest

    client, _ = make_client(200, '{"created":1,"data":[{"b64_json":"AAA="}]}')
    result = await client.generate_image(
        ImageGenerationRequest(model="sdxl", prompt="a lighthouse")
    )
    assert result.data[0].b64_json == "AAA="


@pytest.mark.asyncio
async def test_async_watch_image_job_stops_at_terminal_state():
    body = (
        'data: {"id":"j1","state":"running","capability":"image"}\n\n'
        'data: {"id":"j1","state":"succeeded","capability":"image"}\n\n'
    )
    client, _ = make_client(200, body, media_type="text/event-stream")
    frames = [f async for f in client.watch_image_job("j1")]
    assert len(frames) == 2
    assert frames[-1].state == "succeeded"


@pytest.mark.asyncio
async def test_async_list_nodes_and_cordon():
    client, _ = make_client(200, '[{"nodeId":"n1"}]')
    nodes = await client.list_nodes()
    assert nodes[0].node_id == "n1"

    cordon_client, transport = make_client(200, "")
    await cordon_client.cordon("n1")
    assert str(transport.requests[0].url).endswith("/api/admin/nodes/n1/cordon")


@pytest.mark.asyncio
async def test_async_probe_discriminates_hub_vs_node():
    hub_client, _ = make_client(
        200, '{"coordinatorVersion":"3.37.0","nodes":[],"models":[]}'
    )
    hub_result = await hub_client.probe()
    assert hub_result.kind == "hub"

    node_client, _ = make_client(
        200,
        '{"mode":"solo","nodeVersion":"3.37.0","capabilities":["chat"],'
        '"retrieval":{"enabled":false,"rerank":"none"}}',
    )
    node_result = await node_client.probe()
    assert node_result.kind == "solo_node"
    assert node_result.node_status.retrieval.rerank == "none"


@pytest.mark.asyncio
async def test_async_stream_speech_yields_delta_and_done():
    import base64

    from inferhub_client import SpeechRequest

    audio_b64 = base64.b64encode(b"\x00").decode()
    body = (
        f'event: speech.audio.delta\ndata: {{"audio":"{audio_b64}"}}\n\n'
        'event: speech.audio.done\ndata: {"usage":'
        '{"input_tokens":0,"output_tokens":0,"total_tokens":0}}\n\n'
    )
    client, _ = make_client(200, body, media_type="text/event-stream")
    chunks = [
        c async for c in client.stream_speech(SpeechRequest(model="piper", input="hi"))
    ]
    assert len(chunks) == 2
    assert chunks[0].audio == b"\x00"
    assert chunks[1].type == "speech.audio.done"
