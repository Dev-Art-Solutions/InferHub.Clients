using System.Net;

namespace InferHub.Client.Tests;

/// <summary>
/// Test double for streaming NDJSON responses. Wraps the supplied lines in an
/// <see cref="HttpContent"/> whose <see cref="Stream.ReadAsync(byte[], int, int, CancellationToken)"/>
/// implementation blocks on a <see cref="TaskCompletionSource"/> until <see cref="ReleaseNextLine"/>
/// is called — so tests can interleave "yield a chunk" with "cancel the token" and observe both.
/// </summary>
internal sealed class StreamingHttpMessageHandler : HttpMessageHandler
{
    private readonly ControlledStream stream;

    public StreamingHttpMessageHandler()
    {
        stream = new ControlledStream();
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string> RequestBodies { get; } = new();

    /// <summary>Custom response headers (e.g. <c>X-InferHub-Sources</c>) attached to the reply.</summary>
    public Dictionary<string, string> ResponseHeaders { get; } = new();

    /// <summary>Enqueue a line — becomes visible to the next ReadAsync call.</summary>
    public void EnqueueLine(string line) => stream.EnqueueLine(line);

    /// <summary>Signal end-of-stream; the reader will observe null on the next ReadLineAsync.</summary>
    public void Complete() => stream.Complete();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        };

        foreach (var (name, value) in ResponseHeaders)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    private sealed class ControlledStream : Stream
    {
        private readonly Queue<byte[]> pending = new();
        private readonly Lock gate = new();
        private TaskCompletionSource<bool> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[]? current;
        private int currentOffset;
        private bool completed;

        public void EnqueueLine(string line)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(line + "\n");
            TaskCompletionSource<bool> toSignal;
            lock (gate)
            {
                pending.Enqueue(bytes);
                toSignal = signal;
            }
            toSignal.TrySetResult(true);
        }

        public void Complete()
        {
            TaskCompletionSource<bool> toSignal;
            lock (gate)
            {
                completed = true;
                toSignal = signal;
            }
            toSignal.TrySetResult(true);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (current is not null)
                {
                    var take = Math.Min(current.Length - currentOffset, buffer.Length);
                    current.AsMemory(currentOffset, take).CopyTo(buffer);
                    currentOffset += take;
                    if (currentOffset >= current.Length)
                    {
                        current = null;
                        currentOffset = 0;
                    }
                    return take;
                }

                TaskCompletionSource<bool> waitTcs;
                lock (gate)
                {
                    if (pending.Count > 0)
                    {
                        current = pending.Dequeue();
                        currentOffset = 0;
                        continue;
                    }

                    if (completed)
                    {
                        return 0;
                    }

                    if (signal.Task.IsCompleted)
                    {
                        signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    waitTcs = signal;
                }

                using (cancellationToken.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetCanceled(), waitTcs))
                {
                    await waitTcs.Task.ConfigureAwait(false);
                }
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
