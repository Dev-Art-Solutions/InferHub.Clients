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

/**
 * Groups an SSE line stream into `{event, data}` pairs on each blank-line frame boundary — the
 * mechanics phase-9's dotnet `SseFrameReader` and python's `parse_sse_lines` share between the
 * speech, image-job and admin streams. Built on {@link readNdjsonLines} (D4): the byte decoding
 * and line splitting is written once regardless of which line discipline — NDJSON or SSE — sits
 * on top of it. `data:` lines accumulate (multi-line payloads join with `\n`); a comment line
 * (`:`-prefixed) and any other field are ignored. A frame with no `data:` is skipped rather than
 * yielded as `{data: {}}`, since every InferHub SSE frame this client reads carries a JSON payload.
 */
export async function* readSseFrames(
  stream: ReadableStream<Uint8Array>,
): AsyncGenerator<{ event?: string; data: Record<string, unknown> }> {
  let event: string | undefined;
  let dataLines: string[] = [];

  const emit = (): { event?: string; data: Record<string, unknown> } | undefined => {
    if (dataLines.length === 0) return undefined;
    const raw = dataLines.join("\n");
    let data: Record<string, unknown>;
    try {
      data = JSON.parse(raw) as Record<string, unknown>;
    } catch {
      data = { raw };
    }
    return { event, data };
  };

  for await (const line of readNdjsonLines(stream)) {
    if (line === "") {
      const frame = emit();
      if (frame) yield frame;
      event = undefined;
      dataLines = [];
      continue;
    }
    if (line.startsWith(":")) continue;
    if (line.startsWith("event:")) {
      event = line.slice("event:".length).trim();
    } else if (line.startsWith("data:")) {
      dataLines.push(line.slice("data:".length).trim());
    }
  }
  const trailing = emit();
  if (trailing) yield trailing;
}
