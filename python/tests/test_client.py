import json

import httpx
import pytest

from inferhub_client import (
    ChatMessage,
    ChatRequest,
    EmbeddingsRequest,
    EmbedRequest,
    FileDocument,
    GenerateRequest,
    InferHubClient,
    InferHubError,
    InferHubRetrievalException,
    RetrievalOptions,
    SearchRequest,
    TextDocument,
    VectorQuery,
    VectorUpsert,
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


# -- Phase 17: retrieval -----------------------------------------------------------------------


def test_chat_with_retrieval_sends_the_five_headers():
    client, transport = make_client(
        200,
        '{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true}',
    )
    client.chat(
        ChatRequest(model="llama3", messages=[ChatMessage("user", "hi")]),
        retrieval=RetrievalOptions(
            collection="docs", k=5, model="nomic-embed-text", mode="hybrid", rerank=True
        ),
    )
    sent = transport.requests[0].headers
    assert sent["X-InferHub-Retrieve"] == "docs"
    assert sent["X-InferHub-Retrieve-K"] == "5"
    assert sent["X-InferHub-Retrieve-Model"] == "nomic-embed-text"
    assert sent["X-InferHub-Retrieve-Mode"] == "hybrid"
    assert sent["X-InferHub-Rerank"] == "true"


def test_chat_without_retrieval_sends_no_retrieve_header():
    client, transport = make_client(
        200,
        '{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true}',
    )
    client.chat(ChatRequest(model="llama3", messages=[ChatMessage("user", "hi")]))
    assert "X-InferHub-Retrieve" not in transport.requests[0].headers


def test_424_raises_the_retrieval_specific_exception():
    client, _ = make_client(424, '{"error":"retrieval unavailable"}')
    with pytest.raises(InferHubRetrievalException) as excinfo:
        client.chat(
            ChatRequest(model="llama3", messages=[]),
            retrieval=RetrievalOptions(collection="docs"),
        )
    assert excinfo.value.status_code == 424
    # A caller catching only the base exception still works.
    assert isinstance(excinfo.value, InferHubError)


def test_upsert_and_query_round_trip():
    client, transport = make_client(
        200, '{"id":"v1","vector":[0.1,0.2],"payload":{"text":"hi"}}'
    )
    record = client.upsert(
        "docs", VectorUpsert.from_vector("v1", [0.1, 0.2], {"text": "hi"})
    )
    assert record.id == "v1"
    assert transport.requests[0].url.path == "/api/vector/docs/upsert"

    client2, transport2 = make_client(
        200, '{"matches":[{"id":"v1","score":0.9,"payload":{"text":"hi"}}]}'
    )
    matches = client2.query("docs", VectorQuery.from_vector([0.1, 0.2], top_k=3))
    assert matches[0].id == "v1"
    assert matches[0].score == 0.9
    sent = json.loads(transport2.requests[0].content)
    assert sent["topK"] == 3


def test_get_record_returns_none_on_404():
    client, _ = make_client(404, "")
    assert client.get_record("docs", "missing") is None


def test_delete_record_returns_false_on_404_true_otherwise():
    client, _ = make_client(404, "")
    assert client.delete_record("docs", "missing") is False

    client2, _ = make_client(200, '{"id":"v1","deleted":true}')
    assert client2.delete_record("docs", "v1") is True


def test_ingest_text_returns_ingested_result():
    client, transport = make_client(
        200,
        '{"documentId":"d1","collection":"docs","status":"ingested","chunks":3,'
        '"chunksEmbedded":3,"bytes":42,"contentHash":"abc"}',
    )
    result = client.ingest_text("docs", TextDocument(id="d1", text="hello world"))
    assert result.status == "ingested"
    assert result.chunks_embedded == 3
    sent = json.loads(transport.requests[0].content)
    assert sent["text"] == "hello world"


def test_ingest_text_partial_500_is_returned_not_raised():
    client, _ = make_client(
        500,
        '{"documentId":"z","collection":"handbook","status":"partial","chunks":1,'
        '"chunksEmbedded":0,"bytes":11,"contentHash":"12998c0",'
        '"error":"no node is advertising embedding model \'no-such-embed-model\'"}',
    )
    result = client.ingest_text("handbook", TextDocument(id="z", text="x"))
    assert result.status == "partial"
    assert result.document_id == "z"
    assert result.chunks_embedded == 0


def test_ingest_text_genuine_500_still_raises():
    client, _ = make_client(500, '{"error":"unexpected failure"}')
    with pytest.raises(InferHubError):
        client.ingest_text("docs", TextDocument(id="d1", text="x"))


def test_ingest_file_sends_multipart_with_file_last():
    client, transport = make_client(
        200,
        '{"documentId":"f1","collection":"docs","status":"ingested","chunks":1,'
        '"chunksEmbedded":1,"bytes":5,"contentHash":"x"}',
    )
    result = client.ingest_file(
        "docs",
        FileDocument(
            id="f1", filename="note.txt", stream=b"hello", content_type="text/plain"
        ),
    )
    assert result.status == "ingested"
    request = transport.requests[0]
    assert request.headers["content-type"].startswith("multipart/form-data")


def test_list_and_get_document():
    client, _ = make_client(
        200,
        '{"documents":[{"documentId":"d1","collection":"docs","status":"ingested",'
        '"chunks":2,"bytes":10}]}',
    )
    docs = client.list_documents("docs")
    assert docs[0].document_id == "d1"

    client2, _ = make_client(404, "")
    assert client2.get_document("docs", "missing") is None


def test_get_chunks_index_is_a_string():
    client, _ = make_client(
        200,
        '{"collection":"handbook","documentId":"onboarding","chunks":[{"id":"96117a8f",'
        '"index":"0","page":null,"text":"chunk text"}]}',
    )
    response = client.get_chunks("handbook", "onboarding")
    assert response.chunks[0].index == "0"
    assert response.chunks[0].page is None


def test_delete_document_returns_none_on_404():
    client, _ = make_client(404, "")
    assert client.delete_document("docs", "missing") is None


def test_search_by_string_and_by_request_keeps_wire_order():
    body = (
        '{"collection":"handbook","mode":"hybrid","hits":['
        '{"id":"a","score":0.0163,"documentId":"policy.txt","text":"..."},'
        '{"id":"b","score":0.0325,"documentId":"onboarding","text":"..."}]}'
    )
    client, transport = make_client(200, body)
    result = client.search("handbook", "payroll schedule")
    assert [h.document_id for h in result.hits] == ["policy.txt", "onboarding"]
    sent = json.loads(transport.requests[0].content)
    assert sent["query"] == "payroll schedule"

    client2, _ = make_client(200, body)
    result2 = client2.search(
        "handbook", SearchRequest(query="q", mode="hybrid", rerank=True)
    )
    assert [h.document_id for h in result2.hits] == ["policy.txt", "onboarding"]
