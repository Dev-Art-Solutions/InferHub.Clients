import json

import httpx
import pytest

from inferhub_client import (
    ChatMessage,
    ChatRequest,
    EmbedRequest,
    EmbeddingsRequest,
    GenerateRequest,
    InferHubClient,
    InferHubError,
)

from .conftest import RecordingTransport


def make_client(
    status_code, body, **kwargs
) -> tuple[InferHubClient, RecordingTransport]:
    transport = RecordingTransport(status_code, body, **kwargs)
    http = httpx.Client(base_url="http://localhost:5080/", transport=transport)
    return InferHubClient(http_client=http), transport


def test_list_models_parses_the_tags_response():
    client, _ = make_client(
        200, '{"models":[{"name":"llama3","digest":"abc","size":1234}]}'
    )
    result = client.list_models()
    assert result.models[0].name == "llama3"
    assert result.models[0].digest == "abc"
    assert result.models[0].size == 1234


def test_chat_forces_stream_false_and_parses_the_message():
    client, transport = make_client(
        200,
        '{"model":"llama3","message":{"role":"assistant","content":"pong"},"done":true}',
    )
    result = client.chat(
        ChatRequest(model="llama3", messages=[ChatMessage("user", "ping")], stream=True)
    )
    assert result.message.content == "pong"
    assert result.done is True

    sent = json.loads(transport.requests[0].content)
    assert sent["stream"] is False


def test_chat_surfaces_served_by_and_source_ids_json_array():
    client, _ = make_client(
        200,
        '{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true}',
        headers={
            "X-InferHub-Served-By": "node-1",
            "X-InferHub-Sources": '["doc-1","doc-2"]',
        },
    )
    result = client.chat(
        ChatRequest(model="llama3", messages=[ChatMessage("user", "hi")])
    )
    assert result.served_by == "node-1"
    assert result.source_ids == ["doc-1", "doc-2"]


def test_chat_parses_comma_separated_sources_fallback():
    client, _ = make_client(
        200,
        '{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true}',
        headers={"X-InferHub-Sources": "doc-1,doc-2"},
    )
    result = client.chat(
        ChatRequest(model="llama3", messages=[ChatMessage("user", "hi")])
    )
    assert result.source_ids == ["doc-1", "doc-2"]


def test_chat_stream_yields_deltas_and_stops_at_done():
    body = (
        '{"model":"llama3","message":{"role":"assistant","content":"hel"},"done":false}\n'
        '{"model":"llama3","message":{"role":"assistant","content":"lo"},"done":false}\n'
        '{"model":"llama3","message":{"role":"assistant","content":"!"},"done":true}\n'
    )
    client, transport = make_client(200, body)
    deltas = [
        chunk.message.content
        for chunk in client.chat_stream(ChatRequest(model="llama3", messages=[]))
    ]
    assert deltas == ["hel", "lo", "!"]

    sent = json.loads(transport.requests[0].content)
    assert sent["stream"] is True


def test_chat_stream_raises_on_terminal_error_chunk_with_partial_seen():
    body = (
        '{"model":"llama3","message":{"role":"assistant","content":"partial"},"done":false}\n'
        '{"error":"node dropped mid-stream","done":true}\n'
    )
    client, _ = make_client(200, body)

    seen = []
    with pytest.raises(InferHubError) as excinfo:
        for chunk in client.chat_stream(ChatRequest(model="llama3", messages=[])):
            seen.append(chunk.message.content)

    assert str(excinfo.value) == "node dropped mid-stream"
    assert seen == ["partial"]


def test_generate_blocking_and_streaming():
    client, _ = make_client(200, '{"model":"llama3","response":"pong","done":true}')
    result = client.generate(GenerateRequest(model="llama3", prompt="ping"))
    assert result.response == "pong"

    stream_client, _ = make_client(
        200,
        '{"model":"llama3","response":"a","done":false}\n{"model":"llama3","response":"b","done":true}\n',
    )
    deltas = [
        c.response
        for c in stream_client.generate_stream(
            GenerateRequest(model="llama3", prompt="p")
        )
    ]
    assert deltas == ["a", "b"]


def test_embed_batch_round_trips():
    client, transport = make_client(
        200, '{"model":"nomic-embed-text","embeddings":[[0.1,0.2],[0.3,0.4],[0.5,0.6]]}'
    )
    result = client.embed(EmbedRequest.from_texts("nomic-embed-text", ["a", "b", "c"]))
    assert len(result.embeddings) == 3
    assert result.embeddings[0] == [0.1, 0.2]

    sent = json.loads(transport.requests[0].content)
    assert sent["input"] == ["a", "b", "c"]


def test_embed_raises_on_empty_vector_list():
    client, _ = make_client(200, '{"model":"nomic-embed-text","embeddings":[]}')
    with pytest.raises(InferHubError):
        client.embed(EmbedRequest.from_text("nomic-embed-text", "hi"))


def test_embed_legacy_round_trips_and_raises_on_empty():
    client, _ = make_client(200, '{"embedding":[0.1,0.2,0.3]}')
    result = client.embed_legacy(
        EmbeddingsRequest(model="nomic-embed-text", prompt="hi")
    )
    assert result.embedding == [0.1, 0.2, 0.3]

    empty_client, _ = make_client(200, '{"embedding":[]}')
    with pytest.raises(InferHubError):
        empty_client.embed_legacy(
            EmbeddingsRequest(model="nomic-embed-text", prompt="hi")
        )


def test_get_status_parses_the_snapshot():
    client, _ = make_client(
        200,
        '{"coordinatorVersion":"3.37.0","uptimeSeconds":12.5,'
        '"nodes":[{"nodeId":"n1"}],"models":[{"name":"llama3"}]}',
    )
    status = client.get_status()
    assert status.coordinator_version == "3.37.0"
    assert status.uptime_seconds == 12.5
    assert len(status.nodes) == 1
    assert status.models[0].name == "llama3"


def test_ping_true_on_2xx_false_otherwise():
    up, _ = make_client(200, "OK", media_type="text/plain")
    assert up.ping() is True

    down, _ = make_client(503, "down", media_type="text/plain")
    assert down.ping() is False


def test_404_surfaces_the_hubs_error_message():
    client, _ = make_client(404, '{"error":"model \'nope\' not found"}')
    with pytest.raises(InferHubError) as excinfo:
        client.chat(ChatRequest(model="nope", messages=[]))
    assert excinfo.value.status_code == 404
    assert str(excinfo.value) == "model 'nope' not found"


def test_401_surfaces_as_inferhub_error():
    client, _ = make_client(401, '{"error":"invalid api key"}')
    with pytest.raises(InferHubError) as excinfo:
        client.chat(ChatRequest(model="llama3", messages=[]))
    assert excinfo.value.status_code == 401


def test_503_carries_retry_after_seconds():
    client, _ = make_client(
        503,
        '{"error":"this node does not serve \'embed\' (Node:Capabilities:Disabled)"}',
        headers={"Retry-After": "30"},
    )
    with pytest.raises(InferHubError) as excinfo:
        client.embed(EmbedRequest.from_text("nomic-embed-text", "hi"))
    assert excinfo.value.retry_after == 30.0


def test_bearer_token_is_sent_when_configured():
    # api_key builds the Authorization header via build_headers() when this client constructs its
    # own httpx.Client; injecting http_client (every other test here) bypasses that path entirely,
    # so this is the one test that lets the constructor build its own transport.
    client = InferHubClient("http://localhost:5080/", api_key="secret-key")
    try:
        assert client._http.headers["authorization"] == "Bearer secret-key"
    finally:
        client.close()


def test_context_manager_closes_owned_client():
    with InferHubClient("http://localhost:5080/") as client:
        assert client._http.is_closed is False
    assert client._http.is_closed is True


def test_context_manager_does_not_close_a_caller_supplied_client():
    http = httpx.Client(base_url="http://localhost:5080/")
    with InferHubClient(http_client=http):
        pass
    assert http.is_closed is False
    http.close()
