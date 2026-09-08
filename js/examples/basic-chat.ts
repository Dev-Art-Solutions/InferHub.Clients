/**
 * Blocking chat against a coordinator (or a solo node — same address, same client).
 *
 * INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... npx tsx examples/basic-chat.ts
 * (or: npm run build && node --experimental-strip-types examples/basic-chat.ts on Node ≥ 22)
 */

import { InferHubClient } from "../src/index.js";

const baseUrl = process.env.INFERHUB_BASE ?? "http://localhost:5080/";
const apiKey = process.env.INFERHUB_API_KEY;

const client = new InferHubClient({ baseUrl, apiKey });

const answer = await client.chat({
  model: "llama3",
  messages: [{ role: "user", content: "Say hi in one word." }],
});

console.log(answer.message?.content);
