# InferHub.Client 1.8.1 — text jobs, and the loaded fraction of a group that is already running

1.8.0 added `IInferHubJobsClient` (see its notes) and never reached NuGet: the push was refused because the API key had expired. 1.8.1 is that release plus one fix.

`JobGroup.LoadedFraction` read 16 % for a group that was already running, found by running the Python client from PyPI against a real split model. A member that kept its layers from an earlier load is sent only a part of its share, so "sent of expected" says nothing once the group runs. It is now `null` for a running group.

**Published and verified.** nuget.org no longer accepts the repository's API key, so the release workflow now publishes through Trusted Publishing (a GitHub OIDC token exchanged for a one-hour key; `workflow_dispatch` with a `version` input publishes an existing tag). 1.8.1 was installed from NuGet into a clean project and run against a real coordinator and node (InferHub 3.103.0 code, qwen2.5:0.5b): submit, the status stream with the node's own reports, the wait, the answer (7 tokens, served by `node`), the listing, and dropping the finished job.
