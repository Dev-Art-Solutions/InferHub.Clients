# Social copy — dotnet/v1.0.1 (Iliya posts these manually)

Verify the facts at posting time, not from memory: that 1.0.1 is on nuget.org, that the package
page links `InferHub.Clients`, and that the repository README renders its parity table.

## Facebook

> InferHub.Client 1.0.1 is out, and it is the least interesting release we have shipped on
> purpose — not one line of code changed. What changed is where the code lives.
>
> The repository is now InferHub.Clients: one repository, one hub surface, a client per language.
> C# moved into `dotnet/` alongside `python/`, `js/` and `go/`, which are placeholders today and
> named phases on the roadmap. The package's own metadata was the visible half — 1.0.0 linked a
> repository that had been renamed, and shipped the repository README as its documentation.
>
> The reason is simple. InferHub has grown seven versions of client-facing surface since this
> client froze — the OpenAI dialect, audio in both directions, images, video, ingestion, cloud
> providers, and a node that serves nearly all of it on its own address. Four repositories for
> four readings of one wire is how four clients quietly start disagreeing about it. One
> repository with a shared spec is how they do not.
>
> Package: nuget.org/packages/InferHub.Client
> Code: github.com/Dev-Art-Solutions/InferHub.Clients

## X

> InferHub.Client 1.0.1 — zero code changes, on purpose.
>
> The repo is now InferHub.Clients: one repository, a client per language. C# lives in dotnet/;
> Python, TypeScript and Go are next. Tags are <lang>/vX.Y.Z, because a Go module in a
> subdirectory will not resolve any other way.
>
> github.com/Dev-Art-Solutions/InferHub.Clients

## Notes for the blog post

Slug: `inferhub-clients-monorepo`. `list_posts` first, then create it **visible in one shot** —
the connector is insert-only with a locking slug, so a draft you meant to fix is a post you
cannot fix. **No shell commands in the HTML**: the Cloudflare WAF in front of the blog blocks the
request, not the command, so show the `.csproj` fragment and the layout tree rather than a
`dotnet add package` line inside a `<pre>`. The post lands at
`blog.devart.solutions/blog/inferhub-clients-monorepo`.

Angle: the honest one. This is a plumbing release and the interesting content is *why* a
single-language client repository stops working the moment there is a second language — plus the
tag-scheme detail, which is a genuinely useful thing for anyone publishing a Go module from a
monorepo. Link the repo and the package; the parity table is the picture.
