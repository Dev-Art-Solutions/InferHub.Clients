"""Phase 18 — the admin plane (``/api/admin/*``, an admin key) and the node (a base address, not
a second client — root rule 6). One module because both are "the rest of the surface a caller
reaches with the same object", and because the node's own collection lifecycle
(``/api/collections``) is deliberately not the admin one (dotnet D2/D3): keeping them apart in two
classes would suggest a relationship between the two that does not exist.

Mixed into both :class:`~inferhub_client._client.InferHubClient` and
:class:`~inferhub_client._async_client.AsyncInferHubClient`, same as ``_corpus.py``/``_media.py``.
Admin methods need an **admin** key, not a client key — a caller constructs the same
``InferHubClient`` with an admin key to reach them; unlike dotnet's DI-registered
``IInferHubAdminClient``, Python has no interface-segregation concern forcing a second type (dotnet
rule 9 exists for published-interface stability, which does not apply to a duck-typed class here).
"""

from __future__ import annotations

from typing import AsyncIterator, Iterator, List, Optional

import httpx

from ._base import parse_sse_lines, raise_for_status
from ._models import (
    AdminEvent,
    AdminNode,
    ClientRow,
    CollectionDetail,
    CollectionInfo,
    CollectionsResponse,
    DeleteProfileResult,
    EnsureModelResult,
    FleetModelMatrix,
    InferHubTargetProbe,
    ModelCommandAccepted,
    NodeProfile,
    NodeProfileState,
    PutProfileResult,
    UsageResponse,
)


def _usage_query(from_, to, client_id, model) -> dict:
    params = {}
    if from_ is not None:
        params["from"] = (
            from_.isoformat() if hasattr(from_, "isoformat") else str(from_)
        )
    if to is not None:
        params["to"] = to.isoformat() if hasattr(to, "isoformat") else str(to)
    if client_id is not None:
        params["clientId"] = client_id
    if model is not None:
        params["model"] = model
    return params


class _AdminMethodsMixin:
    """Sync admin + node methods. Mixed into :class:`InferHubClient`; expects ``self._http`` to
    be an ``httpx.Client``."""

    _http: httpx.Client

    # -- Fleet ops --------------------------------------------------------------------------------

    def list_nodes(self) -> List[AdminNode]:
        """``GET /api/admin/nodes``."""
        response = self._http.get("api/admin/nodes")
        raise_for_status(response)
        return [AdminNode.from_json(n) for n in response.json()]

    def cordon(self, node_id: str) -> None:
        """``POST /api/admin/nodes/{node_id}/cordon``."""
        response = self._http.post(f"api/admin/nodes/{node_id}/cordon")
        raise_for_status(response)

    def uncordon(self, node_id: str) -> None:
        """``POST /api/admin/nodes/{node_id}/uncordon``."""
        response = self._http.post(f"api/admin/nodes/{node_id}/uncordon")
        raise_for_status(response)

    def deregister(self, node_id: str) -> None:
        """``POST /api/admin/nodes/{node_id}/deregister``."""
        response = self._http.post(f"api/admin/nodes/{node_id}/deregister")
        raise_for_status(response)

    # -- Vector collections (admin plane) ----------------------------------------------------------

    def list_admin_collections(self) -> CollectionsResponse:
        """``GET /api/admin/vector/collections`` — with replica placement. Not
        :meth:`list_node_collections`: different auth, different route, different shape."""
        response = self._http.get("api/admin/vector/collections")
        raise_for_status(response)
        return CollectionsResponse.from_json(response.json())

    def get_admin_collection(self, collection: str) -> Optional[CollectionDetail]:
        """``GET /api/admin/vector/collections/{collection}`` — ``None`` when it does not exist."""
        response = self._http.get(f"api/admin/vector/collections/{collection}")
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return CollectionDetail.from_json(response.json())

    def create_admin_collection(
        self, name: str, dimension: int, distance: Optional[str] = None
    ) -> CollectionInfo:
        """``POST /api/admin/vector/collections``. Duplicate name → 409, invalid
        name/dimension → 400."""
        body = {"name": name, "dimension": dimension}
        if distance is not None:
            body["distance"] = distance
        response = self._http.post("api/admin/vector/collections", json=body)
        raise_for_status(response)
        return CollectionInfo.from_json(response.json())

    def drop_admin_collection(self, collection: str) -> None:
        """``DELETE /api/admin/vector/collections/{collection}``."""
        response = self._http.delete(f"api/admin/vector/collections/{collection}")
        raise_for_status(response)

    def rebuild_admin_collection(self, collection: str) -> None:
        """``POST /api/admin/vector/collections/{collection}/rebuild``."""
        response = self._http.post(f"api/admin/vector/collections/{collection}/rebuild")
        raise_for_status(response)

    def stream_admin_events(self) -> Iterator[AdminEvent]:
        """``GET /api/admin/stream`` (SSE) — fleet ``snapshot`` events and ``vector.*``
        lifecycle events. Ends when the server closes the stream. No reconnect variant: a caller
        that wants one wraps this generator in their own retry loop, which is a few lines and
        keeps the backoff policy the caller's decision rather than this client's."""
        with self._http.stream("GET", "api/admin/stream") as response:
            raise_for_status(response)
            for event, data in parse_sse_lines(response.iter_lines()):
                yield AdminEvent(event=event, data=data)

    # -- Node profiles ------------------------------------------------------------------------------

    def list_profiles(self) -> List[NodeProfile]:
        """``GET /api/admin/profiles``."""
        response = self._http.get("api/admin/profiles")
        raise_for_status(response)
        return [NodeProfile.from_json(p) for p in response.json()]

    def get_profile(self, name: str) -> Optional[NodeProfile]:
        """``GET /api/admin/profiles/{name}`` — ``None`` when it does not exist."""
        response = self._http.get(f"api/admin/profiles/{name}")
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return NodeProfile.from_json(response.json())

    def put_profile(self, name: str, profile: NodeProfile) -> PutProfileResult:
        """``PUT /api/admin/profiles/{name}`` — creates or replaces. ``profile.name``/
        ``.revision`` are ignored; the hub sets both from the route and its own counter."""
        response = self._http.put(f"api/admin/profiles/{name}", json=profile.to_json())
        raise_for_status(response)
        return PutProfileResult.from_json(response.json())

    def delete_profile(self, name: str) -> DeleteProfileResult:
        """``DELETE /api/admin/profiles/{name}``. Unknown profile → 404."""
        response = self._http.delete(f"api/admin/profiles/{name}")
        raise_for_status(response)
        return DeleteProfileResult.from_json(response.json())

    def get_node_profile(self, node_id: str) -> NodeProfileState:
        """``GET /api/admin/nodes/{node_id}/profile`` — desired beside effective."""
        response = self._http.get(f"api/admin/nodes/{node_id}/profile")
        raise_for_status(response)
        return NodeProfileState.from_json(response.json())

    # -- Model lifecycle ----------------------------------------------------------------------------

    def pull_model(self, node_id: str, model: str) -> ModelCommandAccepted:
        """``POST /api/admin/nodes/{node_id}/models/{model}/pull``. Progress rides
        :meth:`stream_admin_events`'s ``model-progress`` frames."""
        response = self._http.post(f"api/admin/nodes/{node_id}/models/{model}/pull")
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    def delete_model(self, node_id: str, model: str) -> ModelCommandAccepted:
        """``DELETE /api/admin/nodes/{node_id}/models/{model}``."""
        response = self._http.delete(f"api/admin/nodes/{node_id}/models/{model}")
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    def warm_model(self, node_id: str, model: str) -> ModelCommandAccepted:
        """``POST /api/admin/nodes/{node_id}/models/{model}/warm``."""
        response = self._http.post(f"api/admin/nodes/{node_id}/models/{model}/warm")
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    def pull_tool_model(
        self, node_id: str, tool: str, model: str
    ) -> ModelCommandAccepted:
        """``POST /api/admin/nodes/{node_id}/tools/{tool}/models/{model}/pull``."""
        response = self._http.post(
            f"api/admin/nodes/{node_id}/tools/{tool}/models/{model}/pull"
        )
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    def delete_tool_model(
        self, node_id: str, tool: str, model: str
    ) -> ModelCommandAccepted:
        """``DELETE /api/admin/nodes/{node_id}/tools/{tool}/models/{model}``."""
        response = self._http.delete(
            f"api/admin/nodes/{node_id}/tools/{tool}/models/{model}"
        )
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    def list_model_matrix(self) -> FleetModelMatrix:
        """``GET /api/admin/models`` — the fleet-wide model x node matrix."""
        response = self._http.get("api/admin/models")
        raise_for_status(response)
        return FleetModelMatrix.from_json(response.json())

    def ensure_model(
        self, model: str, replicas: Optional[int] = None
    ) -> EnsureModelResult:
        """``POST /api/admin/models/{model}/ensure`` — pulls onto the most suitable
        capable-and-manageable nodes that do not already have it, skipping cordoned ones."""
        params = {"replicas": replicas} if replicas is not None else None
        response = self._http.post(f"api/admin/models/{model}/ensure", params=params)
        raise_for_status(response)
        return EnsureModelResult.from_json(response.json())

    # -- Usage and clients ----------------------------------------------------------------------------

    def query_usage(
        self,
        from_=None,
        to=None,
        client_id: Optional[str] = None,
        model: Optional[str] = None,
    ) -> UsageResponse:
        """``GET /api/admin/usage`` — aggregates only, never a prompt or a completion."""
        response = self._http.get(
            "api/admin/usage", params=_usage_query(from_, to, client_id, model)
        )
        raise_for_status(response)
        return UsageResponse.from_json(response.json())

    def list_clients(self) -> List[ClientRow]:
        """``GET /api/admin/clients`` — never carries a key."""
        response = self._http.get("api/admin/clients")
        raise_for_status(response)
        return [ClientRow.from_json(c) for c in response.json()]

    # -- The node (root rule 6 / roadmap D7): a base address, not a second client -----------------

    def probe(self) -> InferHubTargetProbe:
        """``GET /api/status`` — one round trip, discriminated on whether the body carries
        ``mode`` (a solo node) or not (the hub — its document never has the field)."""
        response = self._http.get("api/status")
        raise_for_status(response)
        return InferHubTargetProbe.from_json(response.json())

    def get_node_version(self) -> str:
        """``GET /api/version`` — **node-only**; a 404 against a hub means "wrong target," not
        "wrong version."."""
        response = self._http.get("api/version")
        raise_for_status(response)
        return response.json().get("version", "")

    def list_node_collections(self) -> List[CollectionInfo]:
        """``GET /api/collections`` — **node-only** vector collection lifecycle; not the
        admin-gated ``/api/admin/vector/collections`` (different auth, different shape, no
        placement/replica info — a node has no fleet to place a replica on)."""
        response = self._http.get("api/collections")
        raise_for_status(response)
        return [
            CollectionInfo.from_json(c)
            for c in response.json().get("collections") or []
        ]

    def get_node_collection(self, name: str) -> Optional[CollectionInfo]:
        """``GET /api/collections/{name}`` — **node-only**. ``None`` when it does not exist."""
        response = self._http.get(f"api/collections/{name}")
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return CollectionInfo.from_json(response.json())

    def create_node_collection(
        self, name: str, dimension: int, distance: Optional[str] = None
    ) -> CollectionInfo:
        """``POST /api/collections`` — **node-only**."""
        body = {"name": name, "dimension": dimension}
        if distance is not None:
            body["distance"] = distance
        response = self._http.post("api/collections", json=body)
        raise_for_status(response)
        return CollectionInfo.from_json(response.json())

    def drop_node_collection(self, name: str) -> None:
        """``DELETE /api/collections/{name}`` — **node-only**."""
        response = self._http.delete(f"api/collections/{name}")
        raise_for_status(response)


class _AsyncAdminMethodsMixin:
    """Async twin of :class:`_AdminMethodsMixin`. Mixed into :class:`AsyncInferHubClient`;
    expects ``self._http`` to be an ``httpx.AsyncClient``."""

    _http: httpx.AsyncClient

    async def list_nodes(self) -> List[AdminNode]:
        response = await self._http.get("api/admin/nodes")
        raise_for_status(response)
        return [AdminNode.from_json(n) for n in response.json()]

    async def cordon(self, node_id: str) -> None:
        response = await self._http.post(f"api/admin/nodes/{node_id}/cordon")
        raise_for_status(response)

    async def uncordon(self, node_id: str) -> None:
        response = await self._http.post(f"api/admin/nodes/{node_id}/uncordon")
        raise_for_status(response)

    async def deregister(self, node_id: str) -> None:
        response = await self._http.post(f"api/admin/nodes/{node_id}/deregister")
        raise_for_status(response)

    async def list_admin_collections(self) -> CollectionsResponse:
        response = await self._http.get("api/admin/vector/collections")
        raise_for_status(response)
        return CollectionsResponse.from_json(response.json())

    async def get_admin_collection(self, collection: str) -> Optional[CollectionDetail]:
        response = await self._http.get(f"api/admin/vector/collections/{collection}")
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return CollectionDetail.from_json(response.json())

    async def create_admin_collection(
        self, name: str, dimension: int, distance: Optional[str] = None
    ) -> CollectionInfo:
        body = {"name": name, "dimension": dimension}
        if distance is not None:
            body["distance"] = distance
        response = await self._http.post("api/admin/vector/collections", json=body)
        raise_for_status(response)
        return CollectionInfo.from_json(response.json())

    async def drop_admin_collection(self, collection: str) -> None:
        response = await self._http.delete(f"api/admin/vector/collections/{collection}")
        raise_for_status(response)

    async def rebuild_admin_collection(self, collection: str) -> None:
        response = await self._http.post(
            f"api/admin/vector/collections/{collection}/rebuild"
        )
        raise_for_status(response)

    async def stream_admin_events(self) -> AsyncIterator[AdminEvent]:
        async with self._http.stream("GET", "api/admin/stream") as response:
            raise_for_status(response)
            event: Optional[str] = None
            data_lines: list = []
            async for line in response.aiter_lines():
                if line == "":
                    if data_lines:
                        import json as _json

                        raw = "\n".join(data_lines)
                        try:
                            data = _json.loads(raw)
                        except (_json.JSONDecodeError, ValueError):
                            data = {"raw": raw}
                        yield AdminEvent(event=event, data=data)
                    event, data_lines = None, []
                    continue
                if line.startswith(":"):
                    continue
                if line.startswith("event:"):
                    event = line[len("event:") :].strip()
                elif line.startswith("data:"):
                    data_lines.append(line[len("data:") :].strip())

    async def list_profiles(self) -> List[NodeProfile]:
        response = await self._http.get("api/admin/profiles")
        raise_for_status(response)
        return [NodeProfile.from_json(p) for p in response.json()]

    async def get_profile(self, name: str) -> Optional[NodeProfile]:
        response = await self._http.get(f"api/admin/profiles/{name}")
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return NodeProfile.from_json(response.json())

    async def put_profile(self, name: str, profile: NodeProfile) -> PutProfileResult:
        response = await self._http.put(
            f"api/admin/profiles/{name}", json=profile.to_json()
        )
        raise_for_status(response)
        return PutProfileResult.from_json(response.json())

    async def delete_profile(self, name: str) -> DeleteProfileResult:
        response = await self._http.delete(f"api/admin/profiles/{name}")
        raise_for_status(response)
        return DeleteProfileResult.from_json(response.json())

    async def get_node_profile(self, node_id: str) -> NodeProfileState:
        response = await self._http.get(f"api/admin/nodes/{node_id}/profile")
        raise_for_status(response)
        return NodeProfileState.from_json(response.json())

    async def pull_model(self, node_id: str, model: str) -> ModelCommandAccepted:
        response = await self._http.post(
            f"api/admin/nodes/{node_id}/models/{model}/pull"
        )
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    async def delete_model(self, node_id: str, model: str) -> ModelCommandAccepted:
        response = await self._http.delete(f"api/admin/nodes/{node_id}/models/{model}")
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    async def warm_model(self, node_id: str, model: str) -> ModelCommandAccepted:
        response = await self._http.post(
            f"api/admin/nodes/{node_id}/models/{model}/warm"
        )
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    async def pull_tool_model(
        self, node_id: str, tool: str, model: str
    ) -> ModelCommandAccepted:
        response = await self._http.post(
            f"api/admin/nodes/{node_id}/tools/{tool}/models/{model}/pull"
        )
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    async def delete_tool_model(
        self, node_id: str, tool: str, model: str
    ) -> ModelCommandAccepted:
        response = await self._http.delete(
            f"api/admin/nodes/{node_id}/tools/{tool}/models/{model}"
        )
        raise_for_status(response)
        return ModelCommandAccepted.from_json(response.json())

    async def list_model_matrix(self) -> FleetModelMatrix:
        response = await self._http.get("api/admin/models")
        raise_for_status(response)
        return FleetModelMatrix.from_json(response.json())

    async def ensure_model(
        self, model: str, replicas: Optional[int] = None
    ) -> EnsureModelResult:
        params = {"replicas": replicas} if replicas is not None else None
        response = await self._http.post(
            f"api/admin/models/{model}/ensure", params=params
        )
        raise_for_status(response)
        return EnsureModelResult.from_json(response.json())

    async def query_usage(
        self,
        from_=None,
        to=None,
        client_id: Optional[str] = None,
        model: Optional[str] = None,
    ) -> UsageResponse:
        response = await self._http.get(
            "api/admin/usage", params=_usage_query(from_, to, client_id, model)
        )
        raise_for_status(response)
        return UsageResponse.from_json(response.json())

    async def list_clients(self) -> List[ClientRow]:
        response = await self._http.get("api/admin/clients")
        raise_for_status(response)
        return [ClientRow.from_json(c) for c in response.json()]

    async def probe(self) -> InferHubTargetProbe:
        response = await self._http.get("api/status")
        raise_for_status(response)
        return InferHubTargetProbe.from_json(response.json())

    async def get_node_version(self) -> str:
        response = await self._http.get("api/version")
        raise_for_status(response)
        return response.json().get("version", "")

    async def list_node_collections(self) -> List[CollectionInfo]:
        response = await self._http.get("api/collections")
        raise_for_status(response)
        return [
            CollectionInfo.from_json(c)
            for c in response.json().get("collections") or []
        ]

    async def get_node_collection(self, name: str) -> Optional[CollectionInfo]:
        response = await self._http.get(f"api/collections/{name}")
        if response.status_code == 404:
            return None
        raise_for_status(response)
        return CollectionInfo.from_json(response.json())

    async def create_node_collection(
        self, name: str, dimension: int, distance: Optional[str] = None
    ) -> CollectionInfo:
        body = {"name": name, "dimension": dimension}
        if distance is not None:
            body["distance"] = distance
        response = await self._http.post("api/collections", json=body)
        raise_for_status(response)
        return CollectionInfo.from_json(response.json())

    async def drop_node_collection(self, name: str) -> None:
        response = await self._http.delete(f"api/collections/{name}")
        raise_for_status(response)
