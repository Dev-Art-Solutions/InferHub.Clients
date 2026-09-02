using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Corpus;

/// <summary>
/// A document uploaded as text — the JSON body of
/// <c>POST /api/collections/{collection}/documents</c>. The hub extracts nothing here: the string
/// is the document.
/// </summary>
/// <remarks>
/// Text and file uploads are two request types because the hub reads two bodies, and it chooses
/// between them by content type rather than by inspecting fields. One record carrying both a
/// string and a stream would put that choice back on the caller and refuse the wrong half at
/// runtime.
/// </remarks>
public sealed class TextDocument
{
    /// <summary>The document itself. Required.</summary>
    /// <remarks>
    /// Chunked and embedded on the fleet, and only the chunks are kept — the hub is not a document
    /// store. Never logged by this library.
    /// </remarks>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The document id. Re-uploading the same id <b>replaces</b> that document's chunks rather than
    /// adding a second copy, which is what makes a revision safe to send twice.
    /// </summary>
    /// <remarks>
    /// Absent, the hub falls back to <see cref="Source"/> and then to the content hash — so a
    /// caller who sets neither gets a document whose id changes with every edit.
    /// </remarks>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Media type of <see cref="Text"/> — <c>text/plain</c> when absent. Set it to
    /// <c>text/markdown</c>, <c>text/html</c> or <c>application/json</c> to get that extractor.
    /// </summary>
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    /// <summary>
    /// Where this text came from, in the caller's own words. Written onto every chunk as
    /// <c>source</c>, and used as the document id when <see cref="Id"/> is absent.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Embedding model for these chunks. Absent takes the hub's
    /// <c>VectorStore:DefaultEmbeddingModel</c>, which must be the one the collection's dimension
    /// was created for.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Extra metadata written onto every chunk, and returned on every search hit. Strings only —
    /// the hub stores chunk metadata as a string map.
    /// </summary>
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}
