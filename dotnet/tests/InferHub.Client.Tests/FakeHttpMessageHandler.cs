using System.Net;

namespace InferHub.Client.Tests;

/// <summary>
/// Test double for <see cref="HttpMessageHandler"/>. Records every request and returns a
/// programmable response body/status. Keeps the tests dependency-free (no Moq).
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode statusCode;
    private readonly string responseBody;
    private readonly string mediaType;

    public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody, string mediaType = "application/json")
    {
        this.statusCode = statusCode;
        this.responseBody = responseBody;
        this.mediaType = mediaType;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string> RequestBodies { get; } = new();

    /// <summary>Custom response headers (e.g. <c>X-InferHub-Sources</c>) attached to every reply.</summary>
    public Dictionary<string, string> ResponseHeaders { get; } = new();

    /// <summary>
    /// Custom <em>content</em> headers (e.g. <c>Content-Disposition</c>). Separate from
    /// <see cref="ResponseHeaders"/> because <see cref="HttpResponseMessage"/> refuses a content
    /// header on the message and vice versa — which is exactly the distinction a client reading
    /// <c>Content-Disposition</c> beside <c>X-InferHub-Served-By</c> has to get right.
    /// </summary>
    public Dictionary<string, string> ContentHeaders { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content is not null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }
        else
        {
            RequestBodies.Add(string.Empty);
        }

        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, mediaType)
        };

        foreach (var (name, value) in ResponseHeaders)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        foreach (var (name, value) in ContentHeaders)
        {
            response.Content.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}
