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

    /// <summary>
    /// Which retrieval mode grounds this call — <c>X-InferHub-Retrieve-Mode</c>, one of
    /// <see cref="Models.Corpus.RetrievalModes"/>. Server default when null.
    /// </summary>
    /// <remarks>
    /// An unknown value is a <c>400</c> from the hub naming the header, not a quiet fall back to
    /// <see cref="Models.Corpus.RetrievalModes.Vector"/> — a caller who asked for hybrid and
    /// silently got vector would draw the wrong conclusion from the answer. On
    /// <c>POST /api/collections/{c}/search</c> the same choice is a <em>body field</em>: search
    /// reads no <c>X-InferHub-*</c> header at all.
    /// </remarks>
    public string? Mode { get; set; }

    /// <summary>
    /// Rerank the retrieved chunks before grounding — <c>X-InferHub-Rerank</c>. Server default
    /// when null.
    /// </summary>
    /// <remarks>
    /// It costs a chat round trip on the hub, and it degrades quietly by design: with no rerank
    /// model resolved, or on a timeout, the hub keeps the original order rather than failing the
    /// call. So <c>true</c> is a request, not a guarantee, and nothing in the answer says whether
    /// it ran.
    /// </remarks>
    public bool? Rerank { get; set; }
}
