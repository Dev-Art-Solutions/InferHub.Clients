using InferHub.Client.Models.Admin;

namespace InferHub.Client;

/// <summary>
/// Admin client for an InferHub coordinator — fleet operations, vector collection
/// lifecycle, and the live admin event stream. Every call needs an <b>admin</b> key
/// (<c>Auth:AdminApiKeys</c>); it is a separate interface so a client key alone never
/// surfaces admin methods. All routes live under <c>/api/admin/*</c> and are audited
/// by the coordinator.
/// </summary>
public interface IInferHubAdminClient
{
    /// <summary>
    /// List every connected node with its admin view (in-flight counts, cordon state,
    /// last audited action) — <c>GET /api/admin/nodes</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AdminNode>> ListNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cordon a node — <c>POST /api/admin/nodes/{nodeId}/cordon</c>. The node stays
    /// connected and finishes in-flight work but receives no new jobs. Unknown node →
    /// <c>404</c>, surfaced as <see cref="Exceptions.InferHubException"/>. See also the
    /// <c>DrainAsync</c> extension for cordon-and-wait.
    /// </summary>
    /// <param name="nodeId">Node id (from <see cref="AdminNode.NodeId"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CordonAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uncordon a node — <c>POST /api/admin/nodes/{nodeId}/uncordon</c>. The node becomes
    /// routable again. Unknown node → <c>404</c>, surfaced as
    /// <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="nodeId">Node id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UncordonAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forcibly disconnect and remove a node — <c>POST /api/admin/nodes/{nodeId}/deregister</c>.
    /// The node's connection is aborted; a node that is still running will typically
    /// re-enroll on its own reconnect schedule. Unknown node → <c>404</c>, surfaced as
    /// <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="nodeId">Node id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeregisterAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all vector collections with their replica placement —
    /// <c>GET /api/admin/vector/collections</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CollectionsResponse> ListCollectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch one collection's detail — <c>GET /api/admin/vector/collections/{collection}</c>:
    /// definition, placement, <see cref="CollectionDetail.UnderReplicated"/> and query stats.
    /// Returns <c>null</c> when the collection does not exist.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CollectionDetail?> GetCollectionAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a collection — <c>POST /api/admin/vector/collections</c>. Duplicate name →
    /// <c>409</c>, invalid name/dimension → <c>400</c>, both surfaced as
    /// <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="name">Collection name.</param>
    /// <param name="dimension">Vector dimension for every record in the collection.</param>
    /// <param name="distance">Distance metric (e.g. <c>cosine</c>); <c>null</c> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CollectionInfo> CreateCollectionAsync(string name, int dimension, string? distance = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drop a collection and its replicas — <c>DELETE /api/admin/vector/collections/{collection}</c>.
    /// Unknown collection → <c>404</c>, surfaced as <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DropCollectionAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Force a heal-to-target replica re-push from the coordinator's raw store —
    /// <c>POST /api/admin/vector/collections/{collection}/rebuild</c>. Unknown collection →
    /// <c>404</c>, surfaced as <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="collection">Collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RebuildAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tail the live admin stream — <c>GET /api/admin/stream</c> (SSE). Yields fleet
    /// <c>snapshot</c> events (on change and as a ~10s keepalive) and <c>vector.*</c>
    /// lifecycle events. Ends when the server closes the stream; use the
    /// <see cref="StreamAdminEventsAsync(AdminStreamOptions, CancellationToken)"/> overload
    /// for automatic reconnect.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read loop; a cancelled token throws promptly.</param>
    IAsyncEnumerable<AdminEvent> StreamAdminEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tail the live admin stream with reconnect/backoff — reconnects when the server
    /// closes the stream or the transport drops, doubling the delay from
    /// <see cref="AdminStreamOptions.InitialBackoff"/> up to
    /// <see cref="AdminStreamOptions.MaxBackoff"/> and resetting it after each received
    /// event. Auth failures (401/403) are never retried and throw
    /// <see cref="Exceptions.InferHubException"/>. The enumerable only completes via
    /// <paramref name="cancellationToken"/> (or stream end when
    /// <see cref="AdminStreamOptions.Reconnect"/> is <c>false</c>).
    /// </summary>
    /// <param name="options">Reconnect behaviour.</param>
    /// <param name="cancellationToken">Cancels the read loop; a cancelled token throws promptly.</param>
    IAsyncEnumerable<AdminEvent> StreamAdminEventsAsync(AdminStreamOptions options, CancellationToken cancellationToken = default);
}
