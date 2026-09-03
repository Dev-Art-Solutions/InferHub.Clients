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

    // ----- Node profiles (phase 13) -----

    /// <summary>List every stored profile — <c>GET /api/admin/profiles</c>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NodeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch one profile — <c>GET /api/admin/profiles/{name}</c>. Returns <c>null</c> when it does
    /// not exist.
    /// </summary>
    /// <param name="name">Profile name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NodeProfile?> GetProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Write a profile — <c>PUT /api/admin/profiles/{name}</c>. Creates it if absent, replaces it
    /// (and bumps its revision) if present. A profile whose <see cref="NodeProfileSelector"/> names
    /// nothing is refused as <c>400</c>, surfaced as <see cref="Exceptions.InferHubException"/> —
    /// this client does not pre-validate that, the hub is the authority (see this type's remarks).
    /// <paramref name="profile"/>'s <see cref="NodeProfile.Name"/> and
    /// <see cref="NodeProfile.Revision"/> are ignored; the hub sets both from the route and its own
    /// counter regardless of what is sent.
    /// </summary>
    /// <param name="name">Profile name (the route segment — this wins over <see cref="NodeProfile.Name"/>).</param>
    /// <param name="profile">The profile definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PutProfileResult> PutProfileAsync(string name, NodeProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a profile — <c>DELETE /api/admin/profiles/{name}</c>. Every node it used to match is
    /// re-asserted and reverts to its own configuration. Unknown profile → <c>404</c>, surfaced as
    /// <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="name">Profile name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DeleteProfileResult> DeleteProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// What one node is actually running against what a profile asked for —
    /// <c>GET /api/admin/nodes/{nodeId}/profile</c>. Desired beside effective, and every refusal the
    /// node reported. Unknown node → <c>404</c>, surfaced as <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="nodeId">Node id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NodeProfileState> GetNodeProfileAsync(string nodeId, CancellationToken cancellationToken = default);

    // ----- Model lifecycle (phase 13) -----

    /// <summary>
    /// Pull a model onto one node — <c>POST /api/admin/nodes/{nodeId}/models/{model}/pull</c>.
    /// Progress rides <c>model-progress</c> frames on
    /// <see cref="StreamAdminEventsAsync(System.Threading.CancellationToken)"/>. Unknown node →
    /// <c>404</c>; a backend that cannot manage models → <c>400</c>, both surfaced as
    /// <see cref="Exceptions.InferHubException"/>.
    /// </summary>
    /// <param name="nodeId">Node id.</param>
    /// <param name="model">Model name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ModelCommandAccepted> PullModelAsync(string nodeId, string model, CancellationToken cancellationToken = default);

    /// <summary>Remove a model from one node — <c>DELETE /api/admin/nodes/{nodeId}/models/{model}</c>.</summary>
    /// <param name="nodeId">Node id.</param>
    /// <param name="model">Model name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ModelCommandAccepted> DeleteModelAsync(string nodeId, string model, CancellationToken cancellationToken = default);

    /// <summary>Load a model into memory on one node — <c>POST /api/admin/nodes/{nodeId}/models/{model}/warm</c>.</summary>
    /// <param name="nodeId">Node id.</param>
    /// <param name="model">Model name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ModelCommandAccepted> WarmModelAsync(string nodeId, string model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pull a model into one tool's own catalogue on one node —
    /// <c>POST /api/admin/nodes/{nodeId}/tools/{tool}/models/{model}/pull</c>. Whether the tool
    /// exists and is allowed is the node's own answer, arriving as a terminal error frame naming the
    /// tool rather than a pre-check here.
    /// </summary>
    /// <param name="nodeId">Node id.</param>
    /// <param name="tool">Tool id.</param>
    /// <param name="model">Model name, in the tool's own catalogue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ModelCommandAccepted> PullToolModelAsync(string nodeId, string tool, string model, CancellationToken cancellationToken = default);

    /// <summary>Remove a model from one tool's catalogue on one node — <c>DELETE /api/admin/nodes/{nodeId}/tools/{tool}/models/{model}</c>.</summary>
    /// <param name="nodeId">Node id.</param>
    /// <param name="tool">Tool id.</param>
    /// <param name="model">Model name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ModelCommandAccepted> DeleteToolModelAsync(string nodeId, string tool, string model, CancellationToken cancellationToken = default);

    /// <summary>
    /// The fleet-wide model × node matrix — <c>GET /api/admin/models</c>. Which nodes hold each
    /// model, and which nodes can manage models at all.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FleetModelMatrix> ListModelMatrixAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensure a model is held by at least <paramref name="replicas"/> nodes —
    /// <c>POST /api/admin/models/{model}/ensure</c>. Pulls onto the most suitable
    /// capable-and-manageable nodes that do not already have it, skipping cordoned ones. The result
    /// carries the hub's full placement reasoning, not just whether it succeeded (this phase's D3).
    /// </summary>
    /// <param name="model">Model name.</param>
    /// <param name="replicas">Desired replica count; defaults to 1 on the hub when omitted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EnsureModelResult> EnsureModelAsync(string model, int? replicas = null, CancellationToken cancellationToken = default);

    // ----- Usage and clients (phase 13) -----

    /// <summary>
    /// Query usage aggregates — <c>GET /api/admin/usage</c>. Aggregates only, and could not carry a
    /// prompt or a completion even if asked (hub 25 D3): the ledger holds counts alone.
    /// </summary>
    /// <param name="from">Inclusive lower bound, or <c>null</c> for no lower bound.</param>
    /// <param name="to">Inclusive upper bound, or <c>null</c> for no upper bound.</param>
    /// <param name="clientId">Filter to one client, or <c>null</c> for every client.</param>
    /// <param name="model">Filter to one model, or <c>null</c> for every model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<UsageResponse> QueryUsageAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? clientId = null,
        string? model = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List every configured named client with its limits and live window consumption —
    /// <c>GET /api/admin/clients</c>. Never a key: see this type's remarks on <see cref="ClientRow"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ClientRow>> ListClientsAsync(CancellationToken cancellationToken = default);
}
