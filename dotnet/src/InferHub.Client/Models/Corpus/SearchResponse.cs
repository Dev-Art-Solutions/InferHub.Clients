using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Corpus;

/// <summary>
/// The ranked chunks a query retrieved — what <c>POST /api/collections/{c}/search</c> answers with.
/// </summary>
public sealed class SearchResponse
{
    /// <summary>The collection that was searched.</summary>
    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>The mode that ran — the request's, or the deployment default it resolved to.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    /// <summary>
    /// The hits, <b>best first, in the order the hub ranked them</b>. An empty list is a real
    /// answer: the collection exists and nothing matched.
    /// </summary>
    /// <remarks>
    /// <b>Do not re-sort this.</b> See <see cref="SearchHit.Score"/> — after a rerank the order and
    /// the score no longer agree, and the order is the one that was asked for.
    /// </remarks>
    [JsonPropertyName("hits")]
    public IReadOnlyList<SearchHit> Hits { get; set; } = Array.Empty<SearchHit>();

    /// <summary>
    /// Which node or provider answered, from <c>X-InferHub-Served-By</c>. Reported, never acted on;
    /// <c>null</c> when the header was absent.
    /// </summary>
    [JsonIgnore]
    public string? ServedBy { get; set; }
}

/// <summary>One retrieved chunk.</summary>
public sealed class SearchHit
{
    /// <summary>The chunk's vector id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// What retrieval scored this chunk — <b>not what ranked it</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rerank reorders the list and leaves every score exactly as retrieval computed it, so on a
    /// reranked answer the hits are deliberately <em>not</em> in score order. Sorting by this field
    /// would silently undo the rerank the caller asked for and paid a chat round trip for.
    /// </para>
    /// <para>
    /// The number also means different things per mode: a similarity in <c>vector</c>, a fused rank
    /// score in <c>hybrid</c>. Reciprocal-rank fusion exists precisely because a cosine distance and
    /// a lexical score share no scale, so comparing scores across modes — or across corpora — is
    /// not meaningful.
    /// </para>
    /// </remarks>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>Which document this chunk came from. Absent for a vector written directly through the data plane.</summary>
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }

    /// <summary>
    /// The 1-based page, when the extractor had pages — a real <c>int</c> here, and a string on the
    /// chunks route, which describes the same chunk.
    /// </summary>
    [JsonPropertyName("page")]
    public int? Page { get; set; }

    /// <summary>
    /// The chunk text, <b>truncated to 280 characters by the hub</b>. This is a snippet for a
    /// citation, not the chunk — read the chunks route when the whole thing is needed.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
