"""Streaming chat, sync and async — the same request, two ways to read it.

INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... python examples/streaming_chat.py
"""

import asyncio
import os

from inferhub_client import (
    AsyncInferHubClient,
    ChatMessage,
    ChatRequest,
    InferHubClient,
)

base_url = os.environ.get("INFERHUB_BASE", "http://localhost:5080/")
api_key = os.environ.get("INFERHUB_API_KEY")


def sync_stream() -> None:
    with InferHubClient(base_url, api_key) as client:
        request = ChatRequest(
            model="llama3", messages=[ChatMessage("user", "Stream me a haiku.")]
        )
        for chunk in client.chat_stream(request):
            print(chunk.message.content or "", end="", flush=True)
        print()


async def async_stream() -> None:
    async with AsyncInferHubClient(base_url, api_key) as client:
        request = ChatRequest(
            model="llama3", messages=[ChatMessage("user", "Stream me a haiku.")]
        )
        async for chunk in client.chat_stream(request):
            print(chunk.message.content or "", end="", flush=True)
        print()


if __name__ == "__main__":
    sync_stream()
    asyncio.run(async_stream())
