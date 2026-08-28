using System.Net;

namespace InferHub.Client.Tests;

/// <summary>
/// Test double that serves a scripted sequence of responses, one per request, in order.
/// Used for flows that make several requests (drain polling, SSE reconnect).
/// </summary>
internal sealed class SequenceHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body, string MediaType)> responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public void Enqueue(HttpStatusCode status, string body, string mediaType = "application/json")
        => responses.Enqueue((status, body, mediaType));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No scripted response left for this request.");
        }

        var (status, body, mediaType) = responses.Dequeue();
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType)
        });
    }
}
