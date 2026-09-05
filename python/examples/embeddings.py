"""Batch and legacy embeddings.

INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... python examples/embeddings.py
"""

import os

from inferhub_client import EmbeddingsRequest, EmbedRequest, InferHubClient

base_url = os.environ.get("INFERHUB_BASE", "http://localhost:5080/")
api_key = os.environ.get("INFERHUB_API_KEY")

with InferHubClient(base_url, api_key) as client:
    batch = client.embed(
        EmbedRequest.from_texts(
            "nomic-embed-text", ["InferHub", "self-hosted", "inference mesh"]
        )
    )
    print(f"{len(batch.embeddings)} vectors, dimension {len(batch.embeddings[0])}")

    legacy = client.embed_legacy(
        EmbeddingsRequest(model="nomic-embed-text", prompt="a single string")
    )
    print(f"legacy: dimension {len(legacy.embedding)}")
