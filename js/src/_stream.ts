/**
 * NDJSON line reader (D3) — the hub's streaming responses are newline-delimited JSON, never actual
 * `text/event-stream`, matching the C# `IAsyncEnumerable<T>` and Python `Iterator`/`AsyncIterator`
 * shape. One line-buffering async generator, shared by `chatStream`/`generateStream`, decodes and
 * splits a `ReadableStream<Uint8Array>` on `\n`, buffering a partial trailing chunk across reads.
 *
 * Considered and rejected: `TextDecoderStream` + a `TransformStream` pipeline (D3) — more
 * "platform-idiomatic" on paper, but the manual buffering here is ~15 lines the corpus already
 * exercises, and it avoids a second code path for runtimes where `TextDecoderStream` lagged
 * `fetch`/`ReadableStream` support.
 */

/**
 * Decodes a byte stream into complete lines, one per yield. The final partial line (no trailing
 * `\n`) is flushed when the stream ends, exactly once, so a body with no trailing newline is not
 * silently dropped.
 */
export async function* readNdjsonLines(
  stream: ReadableStream<Uint8Array>,
): AsyncGenerator<string> {
  const reader = stream.getReader();
  const decoder = new TextDecoder("utf-8");
  let buffer = "";

  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }
      buffer += decoder.decode(value, { stream: true });
      let newlineIndex: number;
      while ((newlineIndex = buffer.indexOf("\n")) !== -1) {
        const line = buffer.slice(0, newlineIndex);
        buffer = buffer.slice(newlineIndex + 1);
        yield line;
      }
    }
    buffer += decoder.decode();
    if (buffer.length > 0) {
      yield buffer;
    }
  } finally {
    reader.releaseLock();
  }
}
