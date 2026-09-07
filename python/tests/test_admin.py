"""Phase 18 — the admin plane (/api/admin/*, an admin key). Same client class as everything
else (see _admin.py's module docstring for why); these tests just point it at admin routes."""

from __future__ import annotations

import json

import httpx
import pytest

from inferhub_client import InferHubError, NodeProfile

from .conftest import RecordingTransport


def make_client(status_code, body, **kwargs):
    transport = RecordingTransport(status_code, body, **kwargs)
    http = httpx.Client(
        base_url="http://localhost:5080/", transport=transport, headers={}
    )
    from inferhub_client import InferHubClient

    return InferHubClient(http_client=http), transport


def test_list_nodes_parses_the_array():
    client, _ = make_client(200, '[{"nodeId":"n1","name":"gpu-1"}]')
    nodes = client.list_nodes()
    assert nodes[0].node_id == "n1"
    assert nodes[0].name == "gpu-1"


def test_cordon_uncordon_deregister_hit_the_right_routes():
    client, transport = make_client(200, "")
    client.cordon("n1")
    client.uncordon("n1")
    client.deregister("n1")
    paths = [str(r.url) for r in transport.requests]
    assert paths[0].endswith("/api/admin/nodes/n1/cordon")
    assert paths[1].endswith("/api/admin/nodes/n1/uncordon")
    assert paths[2].endswith("/api/admin/nodes/n1/deregister")


def test_cordon_unknown_node_is_404():
    client, _ = make_client(404, '{"error":"no such node"}')
    with pytest.raises(InferHubError) as excinfo:
        client.cordon("nope")
    assert excinfo.value.status_code == 404


def test_admin_collections_lifecycle():
    client, _ = make_client(
        200, '{"collections":[{"name":"docs","dimension":768,"distance":"cosine"}]}'
    )
    result = client.list_admin_collections()
    assert result.collections[0].name == "docs"

    detail_client, _ = make_client(
        200, '{"name":"docs","dimension":768,"underReplicated":false}'
    )
    detail = detail_client.get_admin_collection("docs")
    assert detail.under_replicated is False

    missing_client, _ = make_client(404, '{"error":"not found"}')
    assert missing_client.get_admin_collection("nope") is None

    create_client, transport = make_client(
        200, '{"name":"docs","dimension":768,"distance":"cosine"}'
    )
    info = create_client.create_admin_collection("docs", 768, "cosine")
    assert info.name == "docs"
    assert json.loads(transport.requests[0].content) == {
        "name": "docs",
        "dimension": 768,
        "distance": "cosine",
    }


def test_stream_admin_events_parses_snapshot_and_vector_frames():
    body = (
        'event: snapshot\ndata: {"nodes":1}\n\n'
        'event: vector.collection.created\ndata: {"collection":"docs"}\n\n'
    )
    client, _ = make_client(200, body, media_type="text/event-stream")
    events = list(client.stream_admin_events())
    assert [e.event for e in events] == ["snapshot", "vector.collection.created"]
    assert events[1].data == {"collection": "docs"}


def test_profile_round_trip_ignores_name_and_revision_on_write():
    client, transport = make_client(
        200,
        '{"profile":{"name":"gpu-nodes","revision":3,"selector":{"labels":{"gpu":"true"}},'
        '"maxConcurrency":4},"applied":["n1"],"conflicts":[]}',
    )
    profile = NodeProfile(
        name="ignored",
        revision=999,
        selector={"labels": {"gpu": "true"}},
        max_concurrency=4,
    )
    result = client.put_profile("gpu-nodes", profile)
    assert result.profile.name == "gpu-nodes"
    assert result.profile.revision == 3
    assert result.applied == ["n1"]

    sent = json.loads(transport.requests[0].content)
    assert "name" not in sent
    assert "revision" not in sent


def test_delete_profile_reasserts_nodes():
    client, _ = make_client(200, '{"reasserted":["n1","n2"]}')
    result = client.delete_profile("gpu-nodes")
    assert result.reasserted == ["n1", "n2"]


def test_model_lifecycle_returns_the_202_body_with_reused_surfaced():
    client, _ = make_client(
        200,
        '{"nodeId":"n1","model":"llama3","kind":"pull","commandId":"cmd-1","reused":true}',
    )
    accepted = client.pull_model("n1", "llama3")
    assert accepted.command_id == "cmd-1"
    assert accepted.reused is True


def test_ensure_model_returns_the_full_decision_not_a_bool():
    client, transport = make_client(
        200,
        '{"satisfied":false,"decision":{"effectiveTarget":2,"shortfall":1,'
        '"cordonedNodesSkipped":["n3"]}}',
    )
    result = client.ensure_model("llama3", replicas=2)
    assert result.satisfied is False
    assert result.decision["shortfall"] == 1
    assert "replicas=2" in str(transport.requests[0].url)


def test_query_usage_builds_the_filter_query_string():
    client, transport = make_client(200, '{"rows":[]}')
    client.query_usage(client_id="acme", model="llama3")
    url = str(transport.requests[0].url)
    assert "clientId=acme" in url
    assert "model=llama3" in url


def test_query_usage_rows_never_carry_a_key():
    client, _ = make_client(
        200,
        '{"rows":[{"clientId":"acme","model":"llama3","requests":10,"promptTokens":100,'
        '"completionTokens":50,"totalTokens":150,"fallbackRequests":0}]}',
    )
    result = client.query_usage()
    assert result.rows[0].client_id == "acme"
    assert result.rows[0].total_tokens == 150
    assert not hasattr(result.rows[0], "key")


def test_list_clients_never_carries_a_key():
    client, _ = make_client(
        200, '[{"clientId":"acme","limits":{"rpm":60},"liveUsage":{"requests":3}}]'
    )
    rows = client.list_clients()
    assert rows[0].client_id == "acme"
    assert not hasattr(rows[0], "key")
