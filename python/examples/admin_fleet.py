"""A small fleet dashboard: list nodes, write a profile, pull a model, and query usage. Needs an
**admin** key (`Auth:AdminApiKeys`), not a client key — the same `InferHubClient` reaches these
routes, it just needs the right credential (see `_admin.py`'s module docstring for why there is no
separate admin client class in this language).

INFERHUB_BASE=http://localhost:5080/ INFERHUB_ADMIN_KEY=... python examples/admin_fleet.py
"""

import os

from inferhub_client import InferHubClient, NodeProfile

base_url = os.environ.get("INFERHUB_BASE", "http://localhost:5080/")
admin_key = os.environ.get("INFERHUB_ADMIN_KEY")

with InferHubClient(base_url, admin_key) as client:
    for node in client.list_nodes():
        print(f"node {node.node_id!r}: {node.extra}")

    result = client.put_profile(
        "gpu-nodes",
        NodeProfile(selector={"labels": {"gpu": "true"}}, max_concurrency=4),
    )
    print("profile revision:", result.profile.revision if result.profile else None)
    print("applied to:", result.applied)

    matrix = client.list_model_matrix()
    print("models on the fleet:", [m.get("name") for m in matrix.models])

    usage = client.query_usage(model="llama3")
    for row in usage.rows:
        print(f"  {row.client_id}: {row.requests} requests, {row.total_tokens} tokens")
