using InferHub.Client.Models.Admin;

namespace InferHub.Client.Extensions;

/// <summary>Client-side conveniences over <see cref="IInferHubAdminClient"/>.</summary>
public static class InferHubAdminClientExtensions
{
    /// <summary>
    /// Cordon a node and wait for it to drain: polls <see cref="IInferHubAdminClient.ListNodesAsync"/>
    /// until the node reports zero in-flight jobs. Runs entirely client-side — the
    /// coordinator has no drain endpoint. Returns the node's final snapshot, or <c>null</c>
    /// when the node left the fleet while draining (nothing can be in flight on it then).
    /// Waits indefinitely — bound it with <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="client">The admin client.</param>
    /// <param name="nodeId">Node id (from <see cref="AdminNode.NodeId"/>).</param>
    /// <param name="pollInterval">Delay between polls; defaults to 2 seconds.</param>
    /// <param name="cancellationToken">Cancels the wait (the node stays cordoned).</param>
    public static async Task<AdminNode?> DrainAsync(
        this IInferHubAdminClient client,
        string nodeId,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var interval = pollInterval ?? TimeSpan.FromSeconds(2);

        await client.CordonAsync(nodeId, cancellationToken);

        while (true)
        {
            var nodes = await client.ListNodesAsync(cancellationToken);
            var node = nodes.FirstOrDefault(n => string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));

            if (node is null)
            {
                return null;
            }

            if (node.InFlight == 0)
            {
                return node;
            }

            await Task.Delay(interval, cancellationToken);
        }
    }
}
