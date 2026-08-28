namespace InferHub.Client.Models.Admin;

/// <summary>
/// Reconnect behaviour for <see cref="IInferHubAdminClient.StreamAdminEventsAsync(AdminStreamOptions, CancellationToken)"/>.
/// </summary>
public sealed class AdminStreamOptions
{
    /// <summary>
    /// Reconnect (with exponential backoff) when the server closes the stream or the
    /// transport drops. Auth failures (401/403) are never retried. Defaults to <c>true</c>.
    /// Note: <c>vector.*</c> events are live-only and not replayed after a reconnect;
    /// the first event after reconnect is a fresh fleet snapshot.
    /// </summary>
    public bool Reconnect { get; set; } = true;

    /// <summary>First reconnect delay. Doubles per failed attempt. Defaults to 1 second.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Backoff ceiling. Defaults to 30 seconds.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);
}
