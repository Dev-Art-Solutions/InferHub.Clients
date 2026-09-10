/**
 * The async image job seam — submit, watch progress via SSE, then read the picture back once
 * (openImageContent is read-once: a repeat read is a 410).
 *
 * INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... npx tsx examples/images-job.ts
 */

import { InferHubClient } from "../src/index.js";

const baseUrl = process.env.INFERHUB_BASE ?? "http://localhost:5080/";
const apiKey = process.env.INFERHUB_API_KEY;

const client = new InferHubClient({ baseUrl, apiKey });

const job = await client.submitImageGeneration({ model: "sdxl", prompt: "a lighthouse in fog" });
console.log(`job ${job.id}: ${job.state}`);

for await (const update of client.watchImageJob(job.id)) {
  console.log(`  ${update.state} (${update.step ?? "?"}/${update.totalSteps ?? "?"})`);
}

const finished = await client.getImageJob(job.id);
if (finished?.state === "succeeded" && finished.images[0]) {
  const content = await client.openImageContent(job.id, finished.images[0].index ?? 0);
  const bytes = await content.response.arrayBuffer();
  console.log(`picture: ${bytes.byteLength} bytes, ${content.contentType}`);
}
