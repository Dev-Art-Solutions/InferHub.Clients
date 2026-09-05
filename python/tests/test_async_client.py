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
