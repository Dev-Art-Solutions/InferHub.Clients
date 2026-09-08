/**
 * Streaming chat — one ChatResponse per NDJSON line, printed as it arrives.
 *
 * INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... npx tsx examples/streaming-chat.ts
 */

import { InferHubClient, InferHubError } from "../src/index.js";

const baseUrl = process.env.INFERHUB_BASE ?? "http://localhost:5080/";
const apiKey = process.env.INFERHUB_API_KEY;

const client = new InferHubClient({ baseUrl, apiKey });

try {
  for await (const chunk of client.chatStream({
    model: "llama3",
    messages: [{ role: "user", content: "Count from one to five." }],
  })) {
    process.stdout.write(chunk.message?.content ?? "");
    if (chunk.done) {
      console.log(`\n\n(served by: ${chunk.servedBy ?? "unknown"})`);
    }
  }
} catch (error) {
  // A mid-stream terminal error ({"error": ..., "done": true}) throws InferHubError instead of
  // the iterator hanging or ending quietly with a partial answer nobody was told about.
  if (error instanceof InferHubError) {
    console.error(`\nstream terminated: ${error.message}`);
  } else {
    throw error;
  }
}
