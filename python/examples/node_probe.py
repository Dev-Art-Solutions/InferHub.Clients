"""Point the client at any base address and ask which kind of target it is — a coordinator or a
solo node — with one round trip, then use the node-only routes if it turns out to be a node.

INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... python examples/node_probe.py
"""

import os

from inferhub_client import InferHubClient

base_url = os.environ.get("INFERHUB_BASE", "http://localhost:5080/")
api_key = os.environ.get("INFERHUB_API_KEY")

with InferHubClient(base_url, api_key) as client:
    probe = client.probe()
    print("target kind:", probe.kind, "version:", probe.version)

    if probe.kind == "hub":
        print("nodes on the fleet:", len(probe.hub_status.nodes or []))
    else:
        print("node name:", probe.node_status.name)
        print(
            "backend:",
            probe.node_status.backend.name if probe.node_status.backend else None,
        )
        # Node-only routes: /api/collections, not the admin-gated /api/admin/vector/collections.
        for collection in client.list_node_collections():
            print(f"  collection {collection.name!r}: dimension={collection.dimension}")
