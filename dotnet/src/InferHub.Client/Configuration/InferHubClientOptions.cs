namespace InferHub.Client.Configuration;

/// <summary>
/// Configuration for <see cref="IInferHubClient"/>.
/// </summary>
public sealed class InferHubClientOptions
{
    /// <summary>Default coordinator base URL used when none is configured.</summary>
    public const string DefaultBaseAddress = "http://localhost:5080/";

    /// <summary>
    /// Base URL of the InferHub coordinator (e.g. <c>http://localhost:5080/</c>).
    /// </summary>
    public Uri BaseAddress { get; set; } = new(DefaultBaseAddress);

    /// <summary>
    /// Client API key sent as <c>Authorization: Bearer &lt;key&gt;</c>. Not required on
    /// loopback calls unless the coordinator sets <c>Auth:RequireAuthForLoopback=true</c>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Admin API key, sent as the bearer token by <see cref="IInferHubAdminClient"/> only.
    /// The coordinator validates admin routes against a separate key set
    /// (<c>Auth:AdminApiKeys</c>); a client key alone never surfaces admin methods.
    /// </summary>
    public string? AdminApiKey { get; set; }

    /// <summary>
    /// HTTP timeout applied to the underlying <see cref="HttpClient"/>. Defaults to
    /// 100 seconds — inference calls can take a while, tune to your workload.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Number of automatic retries for transient failures. <b>Off by default</b> (<c>0</c>).
    /// Retries apply only to idempotent requests (<c>GET</c>/<c>HEAD</c>) that fail with a
    /// connection error or a <c>5xx</c>/<c>408</c> status — never a streaming or mutating call,
    /// so a chat/generate/upsert is never silently re-run and a stream never retries mid-flight.
    /// Set to a small number (e.g. <c>3</c>) to ride out brief coordinator restarts or blips.
    /// </summary>
    public int MaxRetryAttempts { get; set; }

    /// <summary>
    /// Base delay before the first retry; each subsequent retry doubles it (capped at
    /// <see cref="MaxRetryDelay"/>). Only used when <see cref="MaxRetryAttempts"/> &gt; 0.
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Upper bound on the exponential retry back-off. Only used when <see cref="MaxRetryAttempts"/> &gt; 0.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
