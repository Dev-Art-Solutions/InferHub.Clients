namespace InferHub.Client.Models.Videos;

/// <summary>
/// A finished clip's bytes, as a stream the caller owns — <b>and reads exactly once</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The read unlinks the bytes at the hub.</b> Fetching this content is what expires the clip: a
/// second fetch is a <c>410</c>, a retried fetch after a dropped connection is a <c>410</c>, and the
/// video is gone. That is the hub's contract and this client does not soften it — it refuses to
/// re-send the request that carries it, and hands the stream over rather than buffering somebody's
/// 40 MB of content to be helpful. There is no index: this dialect has no <c>n</c> and a job holds
/// one clip.
/// </para>
/// <para>
/// <b>Dispose it.</b> It holds the live HTTP response; <see cref="Video"/> is that response's body
/// and is valid only until then. <c>await using</c> is the shortest correct form.
/// </para>
/// </remarks>
public sealed class VideoContent : IDisposable, IAsyncDisposable
{
    private readonly HttpResponseMessage response;

    internal VideoContent(
        HttpResponseMessage response,
        Stream video,
        string contentType,
        long? contentLength,
        string? servedBy)
    {
        this.response = response;
        Video = video;
        ContentType = contentType;
        ContentLength = contentLength;
        ServedBy = servedBy;
    }

    /// <summary>The bytes. A live response stream, forward-only, valid until this object is disposed.</summary>
    public Stream Video { get; }

    /// <summary>
    /// The media type the hub sent — <c>video/mp4</c> unless the worker named another. It is the
    /// worker's word for its own output and is not sniffed from the bytes: nothing in this library
    /// decodes a container.
    /// </summary>
    public string ContentType { get; }

    /// <summary>How many bytes, when the hub declared a length.</summary>
    public long? ContentLength { get; }

    /// <summary>Which node answered, from <c>X-InferHub-Served-By</c>. Reported, never interpreted.</summary>
    public string? ServedBy { get; }

    /// <summary>
    /// Read the whole clip into memory. <b>Prefer <c>Video.CopyToAsync(destination)</c></b> — this
    /// exists on top of the stream, never instead of it, and a minute of 720p is tens of megabytes
    /// on the large-object heap.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await Video.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Releases the response and the stream.</summary>
    public void Dispose()
    {
        Video.Dispose();
        response.Dispose();
    }

    /// <summary>Releases the response and the stream.</summary>
    public async ValueTask DisposeAsync()
    {
        await Video.DisposeAsync();
        response.Dispose();
    }
}

/// <summary>
/// How <see cref="IInferHubVideoClient.WatchAsync"/> polls, because <b>there is no SSE for
/// video</b>.
/// </summary>
/// <remarks>
/// The image job seam streams its progress; this one does not, and a video id on the images events
/// route is a <c>404</c> — those routes are scoped to the image capabilities. So the loop is a poll,
/// and this is where its interval is set rather than hard-coded in every caller's code.
/// </remarks>
public sealed class VideoWatchOptions
{
    /// <summary>
    /// How long to wait between polls. Two seconds by default: a render is minutes, and a tighter
    /// loop buys nothing but requests.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether to yield a document even when nothing changed. <c>false</c> by default, so a caller's
    /// loop body runs when there is news — a new status, a new percentage — rather than every two
    /// seconds. The terminal document is always yielded.
    /// </summary>
    public bool YieldUnchanged { get; set; }
}
