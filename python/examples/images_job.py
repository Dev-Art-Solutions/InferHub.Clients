"""Submit a picture as a job, watch it render, then fetch the bytes once (read-once — root rule 7).

INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... python examples/images_job.py
"""

import os

from inferhub_client import ImageGenerationRequest, ImageOptions, InferHubClient

base_url = os.environ.get("INFERHUB_BASE", "http://localhost:5080/")
api_key = os.environ.get("INFERHUB_API_KEY")

with InferHubClient(base_url, api_key) as client:
    job = client.submit_image_generation(
        ImageGenerationRequest(
            model="sdxl",
            prompt="a lighthouse in fog",
            options=ImageOptions(steps=28, seed=42),
        )
    )
    print("queued:", job.id, job.state)

    final = job
    for update in client.watch_image_job(job.id):
        final = update
        print(f"  {update.state} {update.step or 0}/{update.total_steps or 0}")

    if final.state == "succeeded" and final.images:
        content = client.open_image_content(job.id, final.images[0].index or 0)
        with content.response as response:
            with open("lighthouse.png", "wb") as out:
                out.write(response.read())
        print("saved lighthouse.png")
    else:
        print("job ended as:", final.state)
