"""Blocking chat against a coordinator (or a solo node — same address, same client).

INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... python examples/basic_chat.py
"""

import os

from inferhub_client import ChatMessage, ChatRequest, InferHubClient

base_url = os.environ.get("INFERHUB_BASE", "http://localhost:5080/")
api_key = os.environ.get("INFERHUB_API_KEY")

with InferHubClient(base_url, api_key) as client:
    answer = client.chat(
        ChatRequest(
            model="llama3",
            messages=[ChatMessage(role="user", content="Say hi in one word.")],
        )
    )
    print(answer.message.content)
