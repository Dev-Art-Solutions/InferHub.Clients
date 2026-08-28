using InferHub.Client.Configuration;
using InferHub.Client.Http;
using Microsoft.Extensions.DependencyInjection;

namespace InferHub.Client.Extensions;

/// <summary>DI wiring for <see cref="IInferHubClient"/> and <see cref="IInferHubAdminClient"/>.</summary>
public static class InferHubClientServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="IInferHubClient"/> and <see cref="IInferHubAdminClient"/>, each
    /// with its own typed <see cref="HttpClient"/> and bearer-auth <see cref="DelegatingHandler"/>.
    /// The client sends <see cref="InferHubClientOptions.ApiKey"/>; the admin client sends
    /// <see cref="InferHubClientOptions.AdminApiKey"/> and its <see cref="HttpClient"/> has
    /// no overall timeout (the SSE admin stream is long-lived) —
    /// <see cref="InferHubClientOptions.Timeout"/> is applied per non-streaming admin call.
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
