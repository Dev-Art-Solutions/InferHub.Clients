using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Corpus;

/// <summary>
/// A document as the hub can describe it — <c>GET /api/collections/{c}/documents</c> and
/// <c>/{id}</c>.
/// </summary>
/// <remarks>
/// <b>There is no documents table behind this.</b> Every field is read back from the metadata the
/// hub wrote onto the chunks, which is why the count is of chunks that exist rather than chunks
/// that were meant to, and why <see cref="Status"/> cannot go stale. It is also why the original
/// file is not here: the hub keeps chunk text, a hash and metadata, not the document.
/// </remarks>
public sealed class DocumentSummary
{
    /// <summary>The document id.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>How many chunks of it are actually in the store.</summary>
    [JsonPropertyName("chunks")]
    public int Chunks { get; set; }

    /// <summary>Size of the uploaded content in bytes.</summary>
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    /// <summary>Hash of the content that produced these chunks.</summary>
    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// When it was ingested. <c>null</c> when the chunks carry no timestamp — absent, not
    /// substituted with an epoch nobody measured.
    /// </summary>
    [JsonPropertyName("ingestedAt")]
    public DateTimeOffset? IngestedAt { get; set; }

    /// <summary>The file name or source the caller sent, when they sent one.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>The media type the hub extracted it as.</summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>
    /// <see cref="DocumentStatuses.Complete"/> or <see cref="DocumentStatuses.Partial"/> — derived
    /// at read time by comparing the chunks present against the count they claim.
    /// </summary>
    /// <remarks>
    /// These are not the words an <see cref="IngestResult.Status"/> uses. Only <c>partial</c>
    /// appears in both.
    /// </remarks>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Whether chunks of this document are missing from the store.</summary>
    [JsonIgnore]
    public bool IsPartial => string.Equals(Status, DocumentStatuses.Partial, StringComparison.Ordinal);
}

/// <summary>What a stored document's <see cref="DocumentSummary.Status"/> can say.</summary>
public static class DocumentStatuses
{
    /// <summary>Every chunk the document claims is present.</summary>
    public const string Complete = "complete";

    /// <summary>Fewer chunks are present than the document claims — an ingest that failed mid-way.</summary>
    public const string Partial = "partial";
}

/// <summary>The envelope <c>GET /api/collections/{c}/documents</c> answers with.</summary>
public sealed class DocumentsResponse
{
    /// <summary>The collection that was listed.</summary>
    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>Its documents. Empty for a collection that exists and holds none.</summary>
    [JsonPropertyName("documents")]
    public IReadOnlyList<DocumentSummary> Documents { get; set; } = Array.Empty<DocumentSummary>();
}
