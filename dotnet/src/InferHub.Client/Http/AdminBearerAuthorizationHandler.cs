using System.Net.Http.Headers;
using InferHub.Client.Configuration;

namespace InferHub.Client.Http;

/// <summary>
/// Attaches <c>Authorization: Bearer &lt;AdminApiKey&gt;</c> to every outgoing request when
/// an admin key is configured. The coordinator checks admin routes against a separate key
/// set (<c>Auth:AdminApiKeys</c>) — a client key is never accepted there. Callers on
/// loopback with no key set stay unauthenticated (matches the coordinator's default
/// loopback exemption).
/// </summary>
internal sealed class AdminBearerAuthorizationHandler : DelegatingHandler
{
    private readonly InferHubClientOptions options;

    public AdminBearerAuthorizationHandler(InferHubClientOptions options)
    {
        this.options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(options.AdminApiKey) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AdminApiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
