using System.Globalization;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Corpus;

/// <summary>
/// One chunk of a document, as <c>GET /api/collections/{c}/documents/{id}/chunks</c> returns it —
/// what the corpus will actually retrieve, in the order it was cut.
/// </summary>
public sealed class DocumentChunk
{
    /// <summary>
    /// The chunk's vector id. Derived from the document id and the chunk index, which is what makes
    /// re-ingesting a revision replace its chunks instead of layering a second copy underneath.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The 0-based position of this chunk, <b>as a string</b>.
    /// </summary>
    /// <remarks>
    /// It is chunk metadata, and the hub stores chunk metadata as a string map — so this route
    /// hands the number back exactly as it was stored rather than parsing on the way out. A search
    /// hit's <see cref="SearchHit.Page"/> is a real <c>int</c> on the same corpus, which is the
    /// trap: the two routes describe the same chunk in two types. Use <see cref="IndexOrDefault"/>.
    /// </remarks>
    [JsonPropertyName("index")]
    public string? Index { get; set; }

    /// <summary>
    /// The 1-based page this chunk came from, as a string, or <c>null</c>. Only extractors with a
    /// real notion of pages set it — PDF today; a Markdown file has no page and does not pretend to.
    /// </summary>
    [JsonPropertyName("page")]
    public string? Page { get; set; }

    /// <summary>The chunk text. Never logged by this library.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary><see cref="Index"/> parsed invariantly, or <c>null</c> when it is absent or not a number.</summary>
    [JsonIgnore]
    public int? IndexOrDefault => Parse(Index);

    /// <summary><see cref="Page"/> parsed invariantly, or <c>null</c> when the chunk has no page.</summary>
    [JsonIgnore]
    public int? PageOrDefault => Parse(Page);

    private static int? Parse(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}

/// <summary>The envelope the chunks route answers with.</summary>
public sealed class ChunksResponse
{
    /// <summary>The collection the document lives in.</summary>
    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>The document that was read.</summary>
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Its chunks, ordered by index.</summary>
    [JsonPropertyName("chunks")]
    public IReadOnlyList<DocumentChunk> Chunks { get; set; } = Array.Empty<DocumentChunk>();
}

/// <summary>What a delete removed — <c>DELETE /api/collections/{c}/documents/{id}</c>.</summary>
public sealed class DocumentDeletion
{
    /// <summary>The collection it was removed from.</summary>
    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    /// <summary>The document that was removed.</summary>
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Always <c>true</c> on the route that returns this; a document that was not there is a <c>404</c>.</summary>
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    /// <summary>How many chunks were removed with it.</summary>
    [JsonPropertyName("chunks")]
    public int Chunks { get; set; }
}
