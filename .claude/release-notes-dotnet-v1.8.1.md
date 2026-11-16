# InferHub.Client 1.8.1 — text jobs, and the loaded fraction of a group that is already running

1.8.0 added `IInferHubJobsClient` (see its notes) and never reached NuGet: the push was refused because the API key had expired. 1.8.1 is that release plus one fix.

`JobGroup.LoadedFraction` read 16 % for a group that was already running, found by running the Python client from PyPI against a real split model. A member that kept its layers from an earlier load is sent only a part of its share, so "sent of expected" says nothing once the group runs. It is now `null` for a running group.

**Not verified from the registry**: the package is not on NuGet until the key is renewed. The suite (285 passed, 3 skipped per target framework) is what stands behind it.
