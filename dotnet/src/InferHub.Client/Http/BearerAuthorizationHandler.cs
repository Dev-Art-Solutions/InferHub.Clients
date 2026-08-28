using System.Net.Http.Headers;
using InferHub.Client.Configuration;

namespace InferHub.Client.Http;

/// <summary>
/// Attaches <c>Authorization: Bearer &lt;ApiKey&gt;</c> to every outgoing request when
/// an API key is configured. Callers on loopback with no key set stay unauthenticated
/// (matches the coordinator's default loopback exemption).
/// </summary>
internal sealed class BearerAuthorizationHandler : DelegatingHandler
{
    private readonly InferHubClientOptions options;

    public BearerAuthorizationHandler(InferHubClientOptions options)
    {
        this.options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(options.ApiKey) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
