namespace InferHub.Client.Models.Images;

/// <summary>
/// One image out of a finished job, as a stream the caller owns — <b>and reads exactly once</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The read unlinks the bytes at the hub.</b> Fetching this content is what makes the job
/// <c>expired</c>: a second fetch is a <c>410</c>, a retried fetch after a dropped connection is a
/// <c>410</c>, and the picture is gone. The byte you did not keep does not exist anywhere. That is
/// the hub's contract and this client does not soften it — it refuses to retry the request that
/// carries it, and hands the stream over rather than buffering somebody's 40 MB of content to be
/// helpful.
/// </para>
/// <para>
/// <b>Dispose it.</b> It holds the live HTTP response; <see cref="Image"/> is that response's body
/// and is valid only until then. <c>await using</c> is the shortest correct form.
/// </para>
/// </remarks>
public sealed class ImageContent : IDisposable, IAsyncDisposable
{
    private readonly HttpResponseMessage response;

    internal ImageContent(
        HttpResponseMessage response,
        Stream image,
        string contentType,
        long? contentLength,
        string projection,
        string? seamRepair,
        double? seamDelta,
        double? seamDeltaBefore,
        string? servedBy)
    {
        this.response = response;
        Image = image;
        ContentType = contentType;
        ContentLength = contentLength;
        Projection = projection;
        SeamRepair = seamRepair;
        SeamDelta = seamDelta;
        SeamDeltaBefore = seamDeltaBefore;
        ServedBy = servedBy;
    }

    /// <summary>The bytes. A live response stream, forward-only, valid until this object is disposed.</summary>
    public Stream Image { get; }

    /// <summary>The media type the hub sent — <c>image/png</c> for every recipe shipped so far.</summary>
    public string ContentType { get; }

    /// <summary>How many bytes, when the hub declared a length.</summary>
    public long? ContentLength { get; }

    /// <summary>
    /// One of <see cref="ImageProjections"/>, from <c>X-InferHub-Image-Projection</c>. <b>This is
    /// the only place a caller fetching one image can learn it</b> — the JSON that also carries it
    /// is a different request, and one they may never have made. Never inferred from the aspect
    /// ratio: a 2:1 photograph and a 2:1 panorama are the same bytes in the same shape.
    /// </summary>
    public string Projection { get; }

    /// <summary>
    /// The repair mechanism that ran, or <c>null</c> when none was asked for — in which case the
    /// response is identical to one from a hub that never learned to repair seams.
    /// </summary>
    public string? SeamRepair { get; }

    /// <summary>The seam measurement of these bytes, 0–1. Sent only alongside <see cref="SeamRepair"/>.</summary>
    public double? SeamDelta { get; }

    /// <summary>
    /// What that measurement said before the repair ran. Equal to <see cref="SeamDelta"/> when the
    /// repair was discarded for not improving it — which is a real outcome, not a bug.
    /// </summary>
    public double? SeamDeltaBefore { get; }

    /// <summary>Which node answered, from <c>X-InferHub-Served-By</c>. Reported, never interpreted.</summary>
    public string? ServedBy { get; }

    /// <summary>
    /// Read the whole thing into memory. A convenience on top of the stream, never instead of it: a
    /// 4 K render is a large-object-heap allocation, so prefer
    /// <c>Image.CopyToAsync(destination)</c> when the destination is a file or a socket.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await Image.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Releases the response and the stream.</summary>
    public void Dispose()
    {
        Image.Dispose();
        response.Dispose();
    }

    /// <summary>Releases the response and the stream.</summary>
    public async ValueTask DisposeAsync()
    {
        await Image.DisposeAsync();
        response.Dispose();
    }
}
