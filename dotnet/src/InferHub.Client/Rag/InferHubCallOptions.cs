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
}
