using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Corpus;

/// <summary>
/// What an ingest did — how many chunks the document became, how many of them were embedded, and
/// which of the three outcomes it was.
/// </summary>
/// <remarks>
/// <para>
/// <b>A <see cref="IngestStatuses.Partial"/> result arrives with HTTP <c>500</c> and is returned
/// rather than thrown.</b> The hub answers a partial ingest with an error status on purpose — a
/// half-ingested document that claims success is worse than a failure — but the body is complete,
/// the chunks that landed are really in the store, re-posting the same bytes resumes rather than
/// duplicating, and <see cref="Error"/> says what went wrong. A client that mapped every
/// <c>5xx</c> onto an exception would throw away the document id of a document that exists.
/// </para>
/// <para>
/// The three values here are <em>not</em> the two a <see cref="DocumentSummary.Status"/> carries.
/// An ingest reports what this call did (<c>ingested</c>, <c>unchanged</c>, <c>partial</c>); a
/// document reports what is in the store (<c>complete</c>, <c>partial</c>).
/// </para>
/// </remarks>
public sealed class IngestResult
{
    /// <summary>The id the document was stored under — the one to pass to chunks, delete and search.</summary>
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>The collection it went into.</summary>
    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>One of <see cref="IngestStatuses"/>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>How many chunks the document became.</summary>
    [JsonPropertyName("chunks")]
    public int Chunks { get; set; }

    /// <summary>
    /// How many of them have vectors. Equal to <see cref="Chunks"/> unless this is a partial
    /// ingest, and the gap is the part of the document that will not be retrieved.
    /// </summary>
    [JsonPropertyName("chunksEmbedded")]
    public int ChunksEmbedded { get; set; }

    /// <summary>Size of the uploaded content in bytes.</summary>
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    /// <summary>
    /// Hash of the content. Posting the same bytes again is answered
    /// <see cref="IngestStatuses.Unchanged"/> off this hash, with no work done.
    /// </summary>
    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Why the ingest was partial. <c>null</c> on the two successful outcomes — absent rather than
    /// an empty string, because "nothing went wrong" is not a message.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Whether some chunks failed to embed after retries. The document is in the store either way;
    /// re-posting the same bytes resumes the batches that failed.
    /// </summary>
    [JsonIgnore]
    public bool IsPartial => string.Equals(Status, IngestStatuses.Partial, StringComparison.Ordinal);

    /// <summary>Whether the hub found identical bytes already present and did no work.</summary>
    [JsonIgnore]
    public bool IsUnchanged => string.Equals(Status, IngestStatuses.Unchanged, StringComparison.Ordinal);
}

/// <summary>The three outcomes of an ingest call, as the hub spells them.</summary>
public static class IngestStatuses
{
    /// <summary>The document was chunked, embedded and written.</summary>
    public const string Ingested = "ingested";

    /// <summary>Identical bytes were already present under this id; nothing was re-embedded.</summary>
    public const string Unchanged = "unchanged";

    /// <summary>
    /// Some batches failed after retries. Arrives with HTTP <c>500</c> and a complete body — see
    /// <see cref="IngestResult"/>.
    /// </summary>
    public const string Partial = "partial";
}
