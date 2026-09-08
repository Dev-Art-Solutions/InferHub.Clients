/**
 * Batch embeddings — POST /api/embed, a string or a list of strings.
 *
 * INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... npx tsx examples/embeddings.ts
 */

import { InferHubClient } from "../src/index.js";

const baseUrl = process.env.INFERHUB_BASE ?? "http://localhost:5080/";
const apiKey = process.env.INFERHUB_API_KEY;

const client = new InferHubClient({ baseUrl, apiKey });

const result = await client.embed({
  model: "nomic-embed-text:latest",
  input: ["Payroll runs on the fifth working day.", "The office closes at six."],
});

console.log(`${result.embeddings.length} vectors, ${result.embeddings[0]?.length ?? 0} dims each`);
