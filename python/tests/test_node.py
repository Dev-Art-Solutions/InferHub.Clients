"""Phase 18 — the node: a base address, not a second client (root rule 6). `probe()` and the
five node-only methods, driven against the same recorded bodies the conformance corpus uses."""

from __future__ import annotations

import httpx
import pytest

from inferhub_client import InferHubError

from .conftest import RecordingTransport

_NODE_STATUS_BODY = (
    '{"mode":"solo","nodeVersion":"3.37.0","nowUtc":"2026-09-05T00:00:00Z","name":"local-node",'
    '"backend":{"name":"ollama","endpoint":"http://host.docker.internal:11434/","health":null},'
    '"concurrency":null,"gpu":{"cuda":false,"devices":0,"names":[]},'
    '"capabilities":["chat","embed"],"retrieval":{"enabled":true,"provider":"local",'
    '"embeddingModel":"nomic-embed-text","mode":"vector","rerank":"none","collections":[]},'
    '"models":[]}'
)

_HUB_STATUS_BODY = (
    '{"coordinatorVersion":"3.37.0","nowUtc":"2026-09-05T00:00:00Z","uptimeSeconds":12.5,'
    '"nodes":[{"nodeId":"n1","name":"gpu-1"}],"models":[{"name":"llama3"}]}'
)


def make_client(status_code, body, **kwargs):
    transport = RecordingTransport(status_code, body, **kwargs)
    http = httpx.Client(base_url="http://localhost:5080/", transport=transport)
    from inferhub_client import InferHubClient

    return InferHubClient(http_client=http), transport


def test_probe_discriminates_on_the_mode_field_solo_node():
    client, _ = make_client(200, _NODE_STATUS_BODY)
    result = client.probe()
    assert result.kind == "solo_node"
    assert result.version == "3.37.0"
    assert result.node_status.name == "local-node"
    # The founding conformance case: rerank is a string mode, never a bool.
    assert result.node_status.retrieval.rerank == "none"
    assert isinstance(result.node_status.retrieval.rerank, str)
    assert result.hub_status is None


def test_probe_discriminates_on_the_mode_field_hub():
    client, _ = make_client(200, _HUB_STATUS_BODY)
    result = client.probe()
    assert result.kind == "hub"
    assert result.version == "3.37.0"
    assert len(result.hub_status.nodes) == 1
    assert result.node_status is None


def test_get_node_version():
    client, _ = make_client(200, '{"version":"3.37.0"}')
    assert client.get_node_version() == "3.37.0"


def test_node_collections_lifecycle():
    client, _ = make_client(
        200,
        '{"collections":[{"name":"docs","dimension":768,"distance":"cosine",'
        '"recordCount":412,"operations":890}]}',
    )
    collections = client.list_node_collections()
    assert collections[0].name == "docs"
    assert collections[0].record_count == 412

    missing_client, _ = make_client(404, '{"error":"not found"}')
    assert missing_client.get_node_collection("nope") is None

    create_client, _ = make_client(
        200, '{"name":"new-docs","dimension":768,"distance":"cosine"}'
    )
    created = create_client.create_node_collection("new-docs", 768, "cosine")
    assert created.name == "new-docs"

    drop_client, _ = make_client(200, "")
    drop_client.drop_node_collection("new-docs")


def test_node_collections_are_a_different_route_from_admin_collections():
    client, transport = make_client(
        200, '{"collections":[{"name":"docs","dimension":768}]}'
    )
    client.list_node_collections()
    assert str(transport.requests[0].url).endswith("/api/collections")


def test_create_node_collection_duplicate_name_is_409():
    client, _ = make_client(409, '{"error":"collection already exists"}')
    with pytest.raises(InferHubError) as excinfo:
        client.create_node_collection("docs", 768)
    assert excinfo.value.status_code == 409
