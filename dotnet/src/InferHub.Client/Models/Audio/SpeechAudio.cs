namespace InferHub.Client.Models.Audio;

/// <summary>
/// A synthesised answer, as a stream the caller owns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dispose it.</b> It holds the live HTTP response; <see cref="Audio"/> is that response's body
/// and is valid only until then. <c>await using</c> is the shortest correct form.
/// </para>
/// <para>
/// This is what both shapes of the call return. With <c>stream_format</c> absent the hub writes the
/// whole file and the stream ends when it does; with <see cref="SpeechStreamFormats.Audio"/> the
/// bytes arrive as they are made and the same code plays or copies them sooner. Nothing about the
/// caller's side differs, because this library never buffered somebody's audio to be friendly —
/// <see cref="ReadAllBytesAsync"/> is there for whoever genuinely wanted bytes.
/// </para>
/// </remarks>
public sealed class SpeechAudio : IDisposable, IAsyncDisposable
{
    private readonly HttpResponseMessage response;

    internal SpeechAudio(
        HttpResponseMessage response,
        Stream audio,
        string contentType,
        string? fileName,
        string? servedBy,
        int? sampleRate,
        long? characters)
    {
        this.response = response;
        Audio = audio;
        ContentType = contentType;
        FileName = fileName;
        ServedBy = servedBy;
        SampleRate = sampleRate;
        Characters = characters;
    }

    /// <summary>
    /// The audio. A live response stream, forward-only, valid until this object is disposed.
    /// </summary>
    public Stream Audio { get; }

    /// <summary>
    /// The container's media type — <c>audio/wav</c>, <c>audio/mpeg</c>, <c>audio/ogg</c>,
    /// <c>audio/flac</c>, or <c>audio/pcm</c> for the headerless form, which is not a registered
    /// type and which OpenAI's own API has no better answer for either.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// The file name the hub suggested (<c>speech.wav</c>), or <c>null</c> — a streamed answer has
    /// no <c>Content-Disposition</c> to carry one.
    /// </summary>
    public string? FileName { get; }

    /// <summary>
    /// Which node answered, from <c>X-InferHub-Served-By</c>. Reported, never interpreted.
    /// </summary>
    public string? ServedBy { get; }

    /// <summary>
    /// The sample rate the worker measured off its own first chunk, from
    /// <c>X-InferHub-Audio-Sample-Rate</c>. <b>Sent only on a streamed answer</b>, so it is
    /// <c>null</c> for the buffered one — where a <c>wav</c> carries its own header and a
    /// <c>pcm</c> caller already knew the voice.
    /// </summary>
    public int? SampleRate { get; }

    /// <summary>
    /// What was metered, from <c>X-InferHub-Speech-Characters</c>: <b>input characters, not
    /// tokens</b>. Sent only on a streamed answer. This is the number that reconciles with a bill.
    /// </summary>
    public long? Characters { get; }

    /// <summary>
    /// Read the whole thing into memory. A convenience on top of the stream, never instead of it:
    /// at three minutes of wav this is a large-object-heap allocation, so prefer
    /// <c>Audio.CopyToAsync(destination)</c> when the destination is a file or a socket.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await Audio.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Releases the response and the stream.</summary>
    public void Dispose()
    {
        Audio.Dispose();
        response.Dispose();
    }

    /// <summary>Releases the response and the stream.</summary>
    public async ValueTask DisposeAsync()
    {
        await Audio.DisposeAsync();
        response.Dispose();
    }
}
