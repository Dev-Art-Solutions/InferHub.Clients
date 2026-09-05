"""Test doubles built on httpx.MockTransport — no extra runtime dependency (mirrors the C# client's
FakeHttpMessageHandler, which is also dependency-free)."""

from __future__ import annotations

from typing import Dict, List, Optional

import httpx


class RecordingTransport(httpx.MockTransport):
    """Returns one fixed response to every request and records what it received, so a test can
    assert on the request body/headers as well as on how the client parsed the response."""

    def __init__(
        self,
        status_code: int,
        body: str,
        *,
        headers: Optional[Dict[str, str]] = None,
        media_type: str = "application/json",
    ) -> None:
        self.requests: List[httpx.Request] = []
        self._status_code = status_code
        self._body = body
        self._headers = {"content-type": media_type, **(headers or {})}
        super().__init__(self._handle)

    def _handle(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)
        return httpx.Response(
            self._status_code, headers=self._headers, content=self._body
        )
