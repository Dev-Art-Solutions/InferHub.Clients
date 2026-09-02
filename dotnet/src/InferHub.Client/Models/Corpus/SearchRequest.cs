using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Corpus;

/// <summary>
/// A query against a collection — <c>POST /api/collections/{collection}/search</c>. The same
/// retrieval the RAG path runs, with the matches visible instead of folded into a prompt.
/// </summary>
/// <remarks>
/// These knobs are <b>body fields here and headers on chat</b>: the same mode and rerank arrive as
/// <c>X-InferHub-Retrieve-Mode</c> and <c>X-InferHub-Rerank</c> when they ride on
/// <c>/api/chat</c>, <c>/api/generate</c> or <c>/v1/*</c>, where they live on
/// <see cref="RetrievalOptions"/>. Search reads no <c>X-InferHub-*</c> header at all.
/// </remarks>
public sealed class SearchRequest
{
    /// <summary>Create an empty request; set <see cref="Query"/> before use.</summary>
    public SearchRequest()
    {
    }

    /// <summary>Create a request for <paramref name="query"/>.</summary>
    /// <param name="query">What to search for.</param>
    public SearchRequest(string query)
    {
        Query = query;
    }

    /// <summary>What to search for. Required; a blank query is a <c>400</c>.</summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// One of <see cref="RetrievalModes"/>. Absent takes the deployment's
    /// <c>VectorStore:Retrieval:Mode</c>, which defaults to <see cref="RetrievalModes.Vector"/>.
    /// </summary>
    /// <remarks>
    /// An unknown mode is a <c>400</c> rather than a quiet fall back to vector, because a caller who
    /// asked for hybrid and silently got vector would draw the wrong conclusion from the results.
    /// </remarks>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>How many hits to return. Absent takes <c>VectorStore:Retrieval:DefaultK</c>.</summary>
    [JsonPropertyName("k")]
    public int? K { get; set; }

    /// <summary>
    /// Rerank the candidates with a chat model before trimming to <see cref="K"/>. Absent takes the
    /// deployment default.
    /// </summary>
    /// <remarks>
    /// A rerank changes the <em>order</em> and not the scores — see <see cref="SearchHit.Score"/>.
    /// It also degrades quietly by design: if no rerank model resolves, or the model times out, the
    /// hub keeps the original order rather than failing the search, so a response gives no signal
    /// that reranking actually ran.
    /// </remarks>
    [JsonPropertyName("rerank")]
    public bool? Rerank { get; set; }

    /// <summary>Chat model used by the reranker. Absent uses the deployment's configured one.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Embedding model used to vectorise <see cref="Query"/>. It must be the model the collection's
    /// dimension was built for; absent takes <c>VectorStore:DefaultEmbeddingModel</c>.
    /// </summary>
    [JsonPropertyName("embeddingModel")]
    public string? EmbeddingModel { get; set; }
}

/// <summary>The retrieval modes the hub accepts, in both the search body and the chat header.</summary>
public static class RetrievalModes
{
    /// <summary>Embedding similarity only. The default.</summary>
    public const string Vector = "vector";

    /// <summary>Lexical matching only — the mode that finds an error code or a part number.</summary>
    public const string Keyword = "keyword";

    /// <summary>Both branches, fused by rank (RRF) rather than by score.</summary>
    public const string Hybrid = "hybrid";
}
