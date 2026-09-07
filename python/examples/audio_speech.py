"""Transcribe a short recording, then synthesise the reply — buffered and streamed are the same
method, because this client never buffers to be friendly (root rule 7 / dotnet phase-9 D2).

INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... python examples/audio_speech.py
"""

import os

from inferhub_client import InferHubClient, SpeechRequest, TranscriptionRequest

base_url = os.environ.get("INFERHUB_BASE", "http://localhost:5080/")
api_key = os.environ.get("INFERHUB_API_KEY")

with InferHubClient(base_url, api_key) as client:
    # A caller would open a real .wav file here; this example ships no audio fixture.
    with open(os.environ.get("INFERHUB_AUDIO_FILE", "sample.wav"), "rb") as audio_file:
        transcription = client.transcribe(
            TranscriptionRequest(
                model="whisper", audio=audio_file, filename="sample.wav"
            )
        )
    print("heard:", transcription.text)

    # Buffered: the whole file, read once the caller is ready. `speech.response` is the caller's
    # to close — a plain `with` block on it does that.
    speech = client.create_speech(
        SpeechRequest(
            model="piper", input=transcription.text or "Hello.", response_format="wav"
        )
    )
    with speech.response as response:
        with open("reply.wav", "wb") as out:
            out.write(response.read())

    # Streamed: the same request, framed as SSE — first sentence playable before the last is made.
    for chunk in client.stream_speech(
        SpeechRequest(model="piper", input="Streamed instead.", response_format="wav")
    ):
        if chunk.usage is not None:
            print("done — characters billed:", chunk.characters)
        elif chunk.audio:
            print(f"got {len(chunk.audio)} bytes from {chunk.served_by}")
