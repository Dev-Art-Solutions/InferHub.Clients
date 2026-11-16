# Text jobs in the clients — social copy (unposted; Iliya posts by hand)

Covered by the hub's post: https://blog.devart.solutions/blog/inferhub-3-103-a-long-answer-is-a-job-you-can-follow
Packages: python 1.1.1 (PyPI), js 1.1.1 (npm), go 1.1.1; dotnet 1.8.1 is tagged, NOT on NuGet until the key is renewed — do not announce C# before that.

## X

The InferHub clients for Python, TypeScript and Go can now send a chat as a job and follow it: status, the machine running it, the text so far, cancel. For a model split across machines, each part's load is in the status.

pip install inferhub-client · npm i inferhub-client · go get github.com/Dev-Art-Solutions/InferHub.Clients/go
