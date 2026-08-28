using System.Net;
using InferHub.Client.Configuration;
using InferHub.Client.Http;

namespace InferHub.Client.Tests;

public class TransientRetryHandlerTests
{
    /// <summary>Inner handler that replays a scripted list of outcomes: a status or a thrown exception.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> steps = new();
        public int Calls { get; private set; }

        public ScriptedHandler Respond(HttpStatusCode status)
        {
            steps.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent("{}") });
            return this;
        }

        public ScriptedHandler Throw()
        {
            steps.Enqueue(() => throw new HttpRequestException("connect failed"));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (steps.Count == 0)
            {
                throw new InvalidOperationException("No scripted step left.");
            }

            return Task.FromResult(steps.Dequeue()());
        }
    }

    private static HttpClient Wrap(ScriptedHandler inner, InferHubClientOptions options)
    {
        var retry = new TransientRetryHandler(options) { InnerHandler = inner };
        return new HttpClient(retry) { BaseAddress = new Uri("http://localhost:5080/") };
    }

    private static InferHubClientOptions Fast(int attempts) => new()
    {
        MaxRetryAttempts = attempts,
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        MaxRetryDelay = TimeSpan.FromMilliseconds(2)
    };

    [Fact]
    public async Task Off_by_default_does_not_retry()
    {
        var inner = new ScriptedHandler().Respond(HttpStatusCode.ServiceUnavailable);
        var http = Wrap(inner, new InferHubClientOptions()); // MaxRetryAttempts = 0

        var response = await http.GetAsync("api/tags");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Retries_5xx_then_succeeds()
    {
        var inner = new ScriptedHandler()
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.OK);
        var http = Wrap(inner, Fast(3));

        var response = await http.GetAsync("api/tags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Retries_connect_failure_then_succeeds()
    {
        var inner = new ScriptedHandler().Throw().Respond(HttpStatusCode.OK);
        var http = Wrap(inner, Fast(3));

        var response = await http.GetAsync("api/tags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Gives_up_after_max_attempts_and_returns_last_response()
    {
        var inner = new ScriptedHandler()
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.ServiceUnavailable);
        var http = Wrap(inner, Fast(2)); // 1 initial + 2 retries = 3 calls

        var response = await http.GetAsync("api/tags");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Never_retries_a_post_even_with_retries_enabled()
    {
        var inner = new ScriptedHandler().Respond(HttpStatusCode.ServiceUnavailable);
        var http = Wrap(inner, Fast(3));

        var response = await http.PostAsync("api/chat", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Does_not_retry_non_transient_4xx()
    {
        var inner = new ScriptedHandler().Respond(HttpStatusCode.NotFound);
        var http = Wrap(inner, Fast(3));

        var response = await http.GetAsync("api/vector/docs/missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }
}
