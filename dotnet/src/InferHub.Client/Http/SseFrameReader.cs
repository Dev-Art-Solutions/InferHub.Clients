using System.Runtime.CompilerServices;
using System.Text;

namespace InferHub.Client.Http;

/// <summary>One server-sent event: the <c>event:</c> name if the sender wrote one, and the joined <c>data:</c> payload.</summary>
internal readonly record struct SseFrame(string? Event, string Data);

/// <summary>
/// The line mechanics of <c>text/event-stream</c>, shared by every surface that reads one.
/// </summary>
/// <remarks>
/// <para>
/// What is shared is exactly this and no more: <c>data:</c> lines accumulate, a comment line is
/// skipped, a blank line ends the frame, and a payload still open at end-of-body is emitted rather
/// than dropped. <b>How a stream ends is not shared</b>, because the two surfaces genuinely differ
/// — <c>/v1/chat/completions</c> stops at a <c>[DONE]</c> sentinel, <c>/v1/audio/speech</c> stops
/// at a named terminal event and sends no sentinel at all. One reader with a mode flag would be two
/// readers sharing a bug, which is the mistake this split exists to avoid.
/// </para>
/// <para>
/// <c>id:</c> and <c>retry:</c> are read and discarded: no InferHub surface sends either, and a
/// reader that failed on them would break the day one did.
/// </para>
/// </remarks>
internal static class SseFrameReader
{
    public static async IAsyncEnumerable<SseFrame> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var data = new StringBuilder();
        string? eventName = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
            {
                // End of body with no blank line after the last frame: emit it rather than lose it.
                if (data.Length > 0)
                {
                    yield return new SseFrame(eventName, data.ToString());
                }

                yield break;
            }

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return new SseFrame(eventName, data.ToString());
                    data.Clear();
                }

                eventName = null;
                continue;
            }

            if (line[0] == ':')
            {
                continue; // comment / keep-alive
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line["data:".Length..].TrimStart());
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
            }
        }
    }
}
