"""Phase 15/16 — drives conformance/cases.json against inferhub_client. Same file the C# runner
reads (dotnet/tests/InferHub.Client.Tests/ConformanceCorpusTests.cs); a case whose `kind` this
client's v0.1.0 surface does not cover yet (retrieval, the node, the OpenAI dialect) is skipped with
a named reason rather than silently omitted — see conformance/README.md for the schema.
"""

from __future__ import annotations

import json
from pathlib import Path

import httpx
import pytest

from inferhub_client import ChatRequest, InferHubError

from .conftest import RecordingTransport

_SUPPORTED_KINDS = {"chat", "chat-stream"}


def _find_cases_file() -> Path:
    here = Path(__file__).resolve()
    for parent in here.parents:
        candidate = parent / "conformance" / "cases.json"
        if candidate.exists():
            return candidate
    raise FileNotFoundError(f"conformance/cases.json not found above {here}")


_CASES = json.loads(_find_cases_file().read_text(encoding="utf-8"))["cases"]


def _client_for(case) -> tuple:
    response = case["response"]
    transport = RecordingTransport(
        response["status"],
        response["body"],
        headers=response.get("headers"),
        media_type=response.get("mediaType", "application/json"),
    )
    http = httpx.Client(base_url="http://localhost:5080/", transport=transport)
    from inferhub_client import InferHubClient

    return InferHubClient(http_client=http), transport


@pytest.mark.parametrize("case", _CASES, ids=[c["id"] for c in _CASES])
def test_case(case):
    kind = case["kind"]
    if kind not in _SUPPORTED_KINDS:
        pytest.skip(
            f"'{kind}' is outside inferhub-client v0.1.0's surface (phase 16 is core-only)"
        )

    assert_kind = case["assert"]["kind"]
    client, _ = _client_for(case)
    request = ChatRequest(model="llama3", messages=[])

    if assert_kind == "throws-retrieval-exception":
        with pytest.raises(InferHubError) as excinfo:
            client.chat(request)
        assert excinfo.value.status_code == 424
        return

    if assert_kind == "source-ids":
        result = client.chat(request)
        assert result.source_ids == case["assert"]["expected"]
        return

    if assert_kind == "stream-terminal-error":
        seen = []
        with pytest.raises(InferHubError) as excinfo:
            for chunk in client.chat_stream(request):
                seen.append(chunk)
        assert len(seen) == case["assert"]["partialChunks"]
        assert str(excinfo.value) == case["assert"]["errorMessage"]
        return

    pytest.skip(f"assert.kind '{assert_kind}' has no Python runner yet")
