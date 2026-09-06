"""Ingest a couple of documents, search them, then ask a grounded question and print which
documents answered it.

INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... python examples/mini_rag.py
"""

import os

from inferhub_client import (
    ChatMessage,
    ChatRequest,
    InferHubClient,
    RetrievalOptions,
    TextDocument,
)

base_url = os.environ.get("INFERHUB_BASE", "http://localhost:5080/")
api_key = os.environ.get("INFERHUB_API_KEY")
collection = os.environ.get("INFERHUB_COLLECTION", "mini-rag-example")

with InferHubClient(base_url, api_key) as client:
    client.ingest_text(
        collection,
        TextDocument(
            id="payroll-policy", text="Payroll runs on the fifth working day."
        ),
    )
    client.ingest_text(
        collection,
        TextDocument(
            id="onboarding", text="New hires get their laptop on their first day."
        ),
    )

    found = client.search(collection, "When does payroll run?")
    for hit in found.hits:
        print(f"{hit.document_id!r} (score {hit.score:.3f}): {hit.text}")

    answer = client.chat(
        ChatRequest(
            model="llama3",
            messages=[ChatMessage(role="user", content="When does payroll run?")],
        ),
        retrieval=RetrievalOptions(collection=collection, k=3),
    )
    print(answer.message.content)
    print("answered from:", answer.source_ids)
