namespace InferHub.Client.Models.Corpus;

/// <summary>
/// A document uploaded as a file — the multipart body of
/// <c>POST /api/collections/{collection}/documents</c>. Text, Markdown, HTML, JSON and PDF.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is serialized as JSON: the fields become form parts, in the order
/// <c>id</c>, <c>metadata</c>, <c>model</c>, then <c>file</c>. That order is not cosmetic — see
/// <see cref="Content"/>.
/// </para>
/// <para>
/// <b>PDF is a <c>415</c> on a standalone node and on a node-owned collection</b>, with a message
/// naming the limitation: the PDF text extractor ships with the coordinator, and the chunks of a
/// node-owned collection are written by the node that owns it. Convert to text or Markdown first.
/// </para>
/// </remarks>
public sealed class FileDocument
{
    /// <summary>
    /// The bytes, read as the request is written. Required, and never buffered into an array by
    /// this library — a 200 MB PDF is a stream, not a <c>byte[]</c>.
    /// </summary>
    /// <remarks>
    /// The stream is written <b>last</b>, after every form field. Above the hub's
    /// <c>Tools:MaxStreamedBytes</c> a multipart request is routed from its leading fields while
    /// the bytes are still arriving, so a field after the file is refused with a <c>400</c> naming
    /// it — and the buffered path below that ceiling tolerates any order, which is exactly what
    /// makes the mistake dangerous.
    /// </remarks>
    public Stream? Content { get; set; }

    /// <summary>
    /// The file name, including its extension. Required.
    /// </summary>
    /// <remarks>
    /// <b>This one is sent, unlike the file name on an image upload.</b> The hub resolves the
    /// extractor from the extension, writes the name onto every chunk as <c>source</c>, and uses it
    /// as the document id when <see cref="Id"/> is absent — so the name lands in the corpus and
    /// comes back on search hits. It is still never written to a log line by this library.
    /// </remarks>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Media type of the file. Absent, the hub resolves it from <see cref="FileName"/>'s extension.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// The document id. Re-uploading the same id <b>replaces</b> that document's chunks rather than
    /// adding a second copy. Absent, the hub uses <see cref="FileName"/>.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Embedding model for these chunks — the <c>model</c> form field. Absent takes the hub's
    /// <c>VectorStore:DefaultEmbeddingModel</c>.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Extra metadata written onto every chunk. Sent as one <c>metadata</c> form field holding a
    /// JSON object of strings; a value the hub cannot parse is a <c>400</c> naming the field.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}
