using InferHub.Client.Configuration;
using InferHub.Client.Http;
using Microsoft.Extensions.DependencyInjection;

namespace InferHub.Client.Extensions;

/// <summary>
/// DI wiring for <see cref="IInferHubClient"/>, <see cref="IInferHubOpenAiClient"/>,
/// <see cref="IInferHubAudioClient"/> and <see cref="IInferHubAdminClient"/>.
/// </summary>
public static class InferHubClientServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="IInferHubClient"/>, <see cref="IInferHubOpenAiClient"/>,
    /// <see cref="IInferHubAudioClient"/> and <see cref="IInferHubAdminClient"/>, each with its own
    /// typed <see cref="HttpClient"/> and bearer-auth <see cref="DelegatingHandler"/>. The
    /// inference clients send <see cref="InferHubClientOptions.ApiKey"/>; the admin client sends
    /// <see cref="InferHubClientOptions.AdminApiKey"/>. The admin and audio clients have
    /// no overall timeout — the admin SSE stream and a streamed synthesis are both long-lived —
    /// and apply <see cref="InferHubClientOptions.Timeout"/> per non-streaming call instead.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configure the options (base address, keys, timeout).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddInferHubClient(this IServiceCollection services, Action<InferHubClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new InferHubClientOptions();
        configure(options);

        if (options.BaseAddress is null)
        {
            throw new ArgumentException($"{nameof(InferHubClientOptions.BaseAddress)} is required.", nameof(configure));
        }

        services.AddSingleton(options);
        services.AddTransient<TransientRetryHandler>(_ => new TransientRetryHandler(options));
        services.AddTransient<BearerAuthorizationHandler>(_ => new BearerAuthorizationHandler(options));
        services.AddTransient<AdminBearerAuthorizationHandler>(_ => new AdminBearerAuthorizationHandler(options));

        // Retry is the outermost handler (no-op unless MaxRetryAttempts > 0), so a retried
        // request still runs through auth; auth only adds the header when absent, so a resend
        // never double-stamps it.
        services.AddHttpClient<IInferHubClient, InferHubClient>(client =>
        {
            client.BaseAddress = EnsureTrailingSlash(options.BaseAddress);
            client.Timeout = options.Timeout;
        })
        .AddHttpMessageHandler<TransientRetryHandler>()
        .AddHttpMessageHandler<BearerAuthorizationHandler>();

        // The second dialect is the same hub, the same address and the same client key — so it
        // shares the options and the handler chain, and differs only in the paths it posts to.
        services.AddHttpClient<IInferHubOpenAiClient, InferHubOpenAiClient>(client =>
        {
            client.BaseAddress = EnsureTrailingSlash(options.BaseAddress);
            client.Timeout = options.Timeout;
        })
        .AddHttpMessageHandler<TransientRetryHandler>()
        .AddHttpMessageHandler<BearerAuthorizationHandler>();

        // Audio shares the address, the key and the handler chain, and differs in one thing that
        // matters: a streamed synthesis is long-lived, so an HttpClient timeout would abort it
        // mid-sentence. Infinite here, and Options.Timeout applied per transcription instead —
        // the same shape the admin client's SSE stream already needed.
        services.AddHttpClient<IInferHubAudioClient, InferHubAudioClient>(client =>
        {
            client.BaseAddress = EnsureTrailingSlash(options.BaseAddress);
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddHttpMessageHandler<TransientRetryHandler>()
        .AddHttpMessageHandler<BearerAuthorizationHandler>();

        services.AddHttpClient<IInferHubAdminClient, InferHubAdminClient>(client =>
        {
            client.BaseAddress = EnsureTrailingSlash(options.BaseAddress);
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddHttpMessageHandler<TransientRetryHandler>()
        .AddHttpMessageHandler<AdminBearerAuthorizationHandler>();

        return services;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var raw = uri.ToString();
        return raw.EndsWith('/') ? uri : new Uri(raw + "/", UriKind.Absolute);
    }
}
