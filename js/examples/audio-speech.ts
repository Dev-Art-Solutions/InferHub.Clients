/**
 * Text-to-speech, buffered — createSpeech() hands back the live Response; the caller consumes
 * .arrayBuffer() itself (root rule 7: read-once content, never buffered by this client).
 *
 * INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... npx tsx examples/audio-speech.ts
 */

import { InferHubClient } from "../src/index.js";

const baseUrl = process.env.INFERHUB_BASE ?? "http://localhost:5080/";
const apiKey = process.env.INFERHUB_API_KEY;

const client = new InferHubClient({ baseUrl, apiKey });

const speech = await client.createSpeech({
  model: "piper",
  input: "InferHub speaks.",
  responseFormat: "wav",
});

const bytes = await speech.response.arrayBuffer();
console.log(`${bytes.byteLength} bytes, servedBy=${speech.servedBy}, sampleRate=${speech.sampleRate}`);
