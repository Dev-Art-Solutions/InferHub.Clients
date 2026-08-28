namespace InferHub.Client;

/// <summary>
/// Opt-in RAG retrieval for a single chat/generate call. Translated into the coordinator's
/// <c>X-InferHub-Retrieve</c> request headers: the coordinator pulls the top matches from
/// <see cref="Collection"/> and grounds the prompt before dispatching it to a node. When a
/// call carries no <see cref="RetrievalOptions"/> the request behaves exactly like a plain
/// chat/generate.
/// </summary>
public sealed class RetrievalOptions
{
    /// <summary>Create empty options; set <see cref="Collection"/> before use.</summary>
    public RetrievalOptions()
    {
    }

    /// <summary>Create options grounded in <paramref name="collection"/>.</summary>
    /// <param name="collection">Collection to retrieve from (required — <c>X-InferHub-Retrieve</c>).</param>
    public RetrievalOptions(string collection)
    {
        Collection = collection;
    }

    /// <summary>
    /// Collection to retrieve from — <c>X-InferHub-Retrieve</c>. Required; a blank value is
    /// rejected before the call is sent.
    /// </summary>
    public string Collection { get; set; } = string.Empty;

    /// <summary>Number of matches to ground with — <c>X-InferHub-Retrieve-K</c>. Server default when null.</summary>
    public int? K { get; set; }

    /// <summary>Embedding model used to vectorise the prompt — <c>X-InferHub-Retrieve-Model</c>. Server default when null.</summary>
    public string? Model { get; set; }
}
