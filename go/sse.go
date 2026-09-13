package inferhub

import (
	"bufio"
	"encoding/json"
	"io"
	"strings"
)

// SSE frame reading (go/v1.0.0) — the mechanics phase-9's dotnet SseFrameReader, python's
// parse_sse_lines and js's readSseFrames share, ported onto the same bufio.Scanner line reader
// stream.go already uses for NDJSON. Built on the same line-buffering primitive (D-equivalent of
// js _stream.ts's readNdjsonLines/readSseFrames split): the byte decoding and line splitting is
// written once regardless of which line discipline sits on top of it.

// SSEFrame is one `{event, data}` frame of an SSE stream — speech, image-job and admin streams all
// use this shape. Data is the frame's JSON payload; a frame whose payload did not parse as JSON
// carries {"raw": <the literal text>} instead of failing the whole stream.
type SSEFrame struct {
	Event string
	Data  JSONDict
}

// sseFrameReader groups a line-oriented SSE body into frames on each blank-line boundary.
// data: lines accumulate (multi-line payloads join with \n); a comment line (:-prefixed) and any
// other field are ignored. A frame with no data: is skipped, since every InferHub SSE frame this
// client reads carries a JSON payload.
type sseFrameReader struct {
	scanner *bufio.Scanner
	closer  io.Closer
	cur     SSEFrame
	err     error
	done    bool
}

func newSSEFrameReader(body io.ReadCloser) *sseFrameReader {
	return &sseFrameReader{scanner: newScanner(body), closer: body}
}

func (r *sseFrameReader) Next() bool {
	if r.done {
		return false
	}
	var event string
	var dataLines []string
	for r.scanner.Scan() {
		line := r.scanner.Text()
		if line == "" {
			if len(dataLines) == 0 {
				continue // a blank line before any data: line is not a frame boundary
			}
			r.cur = frameFromLines(event, dataLines)
			return true
		}
		if strings.HasPrefix(line, ":") {
			continue
		}
		if strings.HasPrefix(line, "event:") {
			event = strings.TrimSpace(strings.TrimPrefix(line, "event:"))
		} else if strings.HasPrefix(line, "data:") {
			dataLines = append(dataLines, strings.TrimSpace(strings.TrimPrefix(line, "data:")))
		}
	}
	if err := r.scanner.Err(); err != nil {
		r.err = err
		r.done = true
		return false
	}
	if len(dataLines) > 0 {
		r.cur = frameFromLines(event, dataLines)
		r.done = true
		return true
	}
	r.done = true
	return false
}

func frameFromLines(event string, dataLines []string) SSEFrame {
	raw := strings.Join(dataLines, "\n")
	var data JSONDict
	if err := json.Unmarshal([]byte(raw), &data); err != nil {
		data = JSONDict{"raw": raw}
	}
	return SSEFrame{Event: event, Data: data}
}

func (r *sseFrameReader) Value() SSEFrame { return r.cur }
func (r *sseFrameReader) Err() error      { return r.err }
func (r *sseFrameReader) Close() error {
	if r.closer == nil {
		return nil
	}
	return r.closer.Close()
}
