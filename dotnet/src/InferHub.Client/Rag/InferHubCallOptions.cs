namespace InferHub.Client;

/// <summary>
/// Per-call options layered on top of a chat/generate request: opt-in RAG retrieval and
/// sticky conversation routing. Both map to <c>X-InferHub-*</c> request headers, so they
/// leave the request body untouched. Pass <c>null</c> (or omit) for a plain call.
/// </summary>
public sealed class InferHubCallOptions
{
    /// <summary>Ground this call in a collection — <c>X-InferHub-Retrieve[-K|-Model]</c>. Null disables retrieval.</summary>
    public RetrievalOptions? Retrieval { get; set; }

    /// <summary>
    /// Opaque conversation id — <c>X-InferHub-Conversation</c>. Sends every turn of a
    /// conversation to the same node (sticky routing). Null lets the coordinator route freely.
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Steer this call to a named cloud provider — <c>X-InferHub-Provider: &lt;id&gt;</c>. A steer can
    /// only ever <b>narrow</b> what the hub's configuration already permits: it cannot create a
    /// route, and a provider that does not serve this model is refused with a <c>400</c> rather
    /// than quietly replaced. Mutually exclusive with <see cref="FleetOnly"/>.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Keep this call on the fleet — <c>X-InferHub-Provider: node</c>. No cloud provider sees the
    /// prompt, on a hub with four providers configured and on a hub with none. Mutually exclusive
    /// with <see cref="Provider"/>; setting both throws.
    /// </summary>
    public bool FleetOnly { get; set; }

    /// <summary>Shorthand for a retrieval-only call.</summary>
    /// <param name="collection">Collection to retrieve from.</param>
    /// <param name="k">Optional match count (<c>X-InferHub-Retrieve-K</c>).</param>
    /// <param name="model">Optional embedding model (<c>X-InferHub-Retrieve-Model</c>).</param>
    public static InferHubCallOptions ForRetrieval(string collection, int? k = null, string? model = null)
        => new() { Retrieval = new RetrievalOptions(collection) { K = k, Model = model } };

    /// <summary>Shorthand for a sticky-routing-only call.</summary>
    /// <param name="conversationId">Opaque conversation id (<c>X-InferHub-Conversation</c>).</param>
    public static InferHubCallOptions ForConversation(string conversationId)
        => new() { ConversationId = conversationId };

    /// <summary>Shorthand for a call steered to one cloud provider.</summary>
    /// <param name="provider">Provider id, as the hub's operator configured it.</param>
    public static InferHubCallOptions ForProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        return new InferHubCallOptions { Provider = provider };
    }

    /// <summary>Shorthand for "no cloud provider sees this prompt" — <c>X-InferHub-Provider: node</c>.</summary>
    public static InferHubCallOptions ForFleetOnly() => new() { FleetOnly = true };
}
