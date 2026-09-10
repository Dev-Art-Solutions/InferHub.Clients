/**
 * A read-only tour of the admin plane — needs an admin key, not a client key.
 *
 * INFERHUB_BASE=http://localhost:5080/ INFERHUB_ADMIN_KEY=... npx tsx examples/admin-fleet.ts
 */

import { InferHubClient } from "../src/index.js";

const baseUrl = process.env.INFERHUB_BASE ?? "http://localhost:5080/";
const apiKey = process.env.INFERHUB_ADMIN_KEY;

const client = new InferHubClient({ baseUrl, apiKey });

const nodes = await client.listNodes();
console.log(`${nodes.length} node(s):`, nodes.map((n) => n.nodeId).join(", "));

const matrix = await client.listModelMatrix();
console.log(`${matrix.models.length} model(s) across ${matrix.nodes.length} node(s)`);

const usage = await client.queryUsage();
console.log(`${usage.rows.length} usage row(s)`);

const clients = await client.listClients();
console.log(`${clients.length} configured client(s)`);
