/**
 * Ingest a couple of documents, search them, then ask a grounded question and print which
 * documents answered it.
 *
 * INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... npx tsx examples/mini-rag.ts
 */

import { InferHubClient } from "../src/index.js";

const baseUrl = process.env.INFERHUB_BASE ?? "http://localhost:5080/";
const apiKey = process.env.INFERHUB_API_KEY;
const collection = process.env.INFERHUB_COLLECTION ?? "mini-rag-example";

const client = new InferHubClient({ baseUrl, apiKey });

await client.ingestText(collection, {
  id: "payroll-policy",
  text: "Payroll runs on the fifth working day.",
});
await client.ingestText(collection, {
  id: "onboarding",
  text: "New hires get their laptop on their first day.",
});

const found = await client.search(collection, "When does payroll run?");
for (const hit of found.hits) {
  console.log(`${hit.documentId} (score ${hit.score.toFixed(3)}): ${hit.text}`);
}

const answer = await client.chat(
  { model: "llama3", messages: [{ role: "user", content: "When does payroll run?" }] },
  { collection, k: 3 },
);
console.log(answer.message?.content);
console.log("answered from:", answer.sourceIds);
