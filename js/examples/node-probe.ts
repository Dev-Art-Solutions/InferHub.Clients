/**
 * probe() tells a coordinator apart from a solo node with one GET /api/status — same address,
 * same client, discriminated on whether the body carries `mode`.
 *
 * INFERHUB_BASE=http://localhost:5080/ INFERHUB_API_KEY=... npx tsx examples/node-probe.ts
 */

import { InferHubClient } from "../src/index.js";

const baseUrl = process.env.INFERHUB_BASE ?? "http://localhost:5080/";
const apiKey = process.env.INFERHUB_API_KEY;

const client = new InferHubClient({ baseUrl, apiKey });

const result = await client.probe();

if (result.kind === "hub") {
  console.log(`hub ${result.version}, ${result.hubStatus?.nodes?.length ?? 0} node(s)`);
} else {
  console.log(
    `solo node ${result.version} (${result.nodeStatus?.name}), ` +
      `capabilities: ${result.nodeStatus?.capabilities.join(", ")}`,
  );
}
