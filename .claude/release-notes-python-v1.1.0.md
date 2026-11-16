# python client 1.1.0 — text jobs

InferHub 3.103 can run a chat or a generate as a **job**: submitted to `/api/jobs`, accepted at once with an id, and
followed by that id while the hub and the node running it update its status. This release adds that surface to the
python client. Nothing published changed.

## New

- Submit a chat or a generate as a job; the call returns at once with the status.
- Read a job, list this client's jobs, and follow a job's status as server-sent events or by polling until it is finished.
- The status: queued, running, succeeded, failed or cancelled; a stage (waiting, sent, preparing, generating, done);
  the node and when it last reported on the job; pieces so far; exact tokens at the end.
- For a model split across several nodes the status carries the group: each member, its layers, and how much of its
  share of the model has been sent to it — with the loaded fraction worked out.
- Read the answer: the finished one, or the text so far while the job still runs. A failed job raises its own error status.
- Cancel a running job, or drop a finished one.

## Why a job

It waits out the hub's 503s (a full fleet, a group reassembling after a member left) instead of answering them, it has
its own longer deadline on the hub, and its answer can be read after the caller reconnects.

## Checked

- The unit suite, against status and result bodies **recorded** from a real coordinator and node (InferHub 3.103.0,
  qwen2.5:0.5b, 2026-11-16). The split-model `group` body and the SSE framing in the tests are **derived** from the
  hub's serializer.
- Installed from the public registry and run against the published InferHub 3.103.0 images with a model split across
  three nodes: see the end of this file.

## Not established

- Hub only: a solo node has no `/api/jobs`, and a submit there raises its 404.
- A hub restart loses every job; a waiter then gets a 404. Not run.
