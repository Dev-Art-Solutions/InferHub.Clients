package inferhub

import (
	"bufio"
	"io"
)

// NDJSON line reading (D2 in plans/phase-22-go-core.md) — the hub's streaming responses are
// newline-delimited JSON, never actual text/event-stream, matching the C# IAsyncEnumerable<T>,
// Python Iterator/AsyncIterator and TypeScript AsyncIterable<T> shape. bufio.Scanner already does
// exactly this buffering (a partial trailing line is held across reads, same as js's _stream.ts
// hand-rolled version); wrapping stdlib is the D5-budget-correct move, not reinventing it, and
// there is no test asserting the *technique* — only that a split line is not dropped.

// ChatStream iterates the chunks of a streaming POST /api/chat response, one ChatResponse per
// NDJSON line. Call Next until it returns false, then check Err. The zero value is not usable;
// obtain one from Client.ChatStream.
//
// D1 — this is a Scanner-shaped iterator (Next/Value/Err/Close), not a Go 1.23 iter.Seq2[T, error]
// range-over-func and not a channel. Three considered:
//   - iter.Seq2[ChatResponse, error]: the most "current" idiom, but it requires Go 1.23 and this
//     package cannot be build-tested in the environment phase 22 was implemented in (no local Go
//     toolchain — see plans/phase-22-go-core.md's verification section) — a generics/range-over-func
//     feature is exactly the kind of syntax an author gets subtly wrong without a compiler to check
//     it against, and go.mod pins 1.22 so the module works for a caller one version behind current.
//   - a channel + goroutine: needs a goroutine per stream and a caller who does not drain it to
//     completion leaks it; the Scanner shape has no concurrency to reason about at all.
//   - a callback (ForEach(func(ChatResponse) error) error): fine for "process every chunk" but
//     awkward for a caller who wants to break early after finding one thing, which `for
//     stream.Next()` does not require the caller to special-case.
//
// The Scanner shape is exactly database/sql.Rows and bufio.Scanner's own shape — the most-used
// iterator idiom in the stdlib itself, and every Go developer already knows the `for rows.Next()`
// loop.
type ChatStream struct {
	scanner   *bufio.Scanner
	closer    io.Closer
	servedBy  string
	sourceIDs []string
	cur       ChatResponse
	err       error
	done      bool
}

// Next advances to the next chunk. It returns false at end of stream, on a transport error, or
// after a terminal error chunk ({"error":...,"done":true}) — check Err to tell them apart.
func (s *ChatStream) Next() bool {
	if s.done {
		return false
	}
	for s.scanner.Scan() {
		line := s.scanner.Text()
		chunk, ok, err := parseNDJSONLine(line)
		if err != nil {
			s.err = err
			s.done = true
			return false
		}
		if !ok {
			continue // blank line
		}
		resp, err := chatResponseFromJSON(chunk)
		if err != nil {
			s.err = err
			s.done = true
			return false
		}
		resp.ServedBy = s.servedBy
		resp.SourceIDs = s.sourceIDs
		s.cur = resp
		if resp.Done != nil && *resp.Done {
			s.done = true
		}
		return true
	}
	if err := s.scanner.Err(); err != nil {
		s.err = err
	}
	s.done = true
	return false
}

// Value returns the chunk most recently produced by Next.
func (s *ChatStream) Value() ChatResponse { return s.cur }

// Err returns the first error encountered, if any — including a mid-stream terminal error chunk
// (root CLAUDE.md testing-discipline rule: a mid-stream error must terminate, never hang).
func (s *ChatStream) Err() error { return s.err }

// Close releases the underlying HTTP response body. Safe to call after Next has returned false,
// and safe to call more than once.
func (s *ChatStream) Close() error {
	if s.closer == nil {
		return nil
	}
	return s.closer.Close()
}

// GenerateStream is ChatStream's twin for POST /api/generate.
type GenerateStream struct {
	scanner   *bufio.Scanner
	closer    io.Closer
	servedBy  string
	sourceIDs []string
	cur       GenerateResponse
	err       error
	done      bool
}

func (s *GenerateStream) Next() bool {
	if s.done {
		return false
	}
	for s.scanner.Scan() {
		line := s.scanner.Text()
		chunk, ok, err := parseNDJSONLine(line)
		if err != nil {
			s.err = err
			s.done = true
			return false
		}
		if !ok {
			continue
		}
		resp, err := generateResponseFromJSON(chunk)
		if err != nil {
			s.err = err
			s.done = true
			return false
		}
		resp.ServedBy = s.servedBy
		resp.SourceIDs = s.sourceIDs
		s.cur = resp
		if resp.Done != nil && *resp.Done {
			s.done = true
		}
		return true
	}
	if err := s.scanner.Err(); err != nil {
		s.err = err
	}
	s.done = true
	return false
}

func (s *GenerateStream) Value() GenerateResponse { return s.cur }
func (s *GenerateStream) Err() error              { return s.err }
func (s *GenerateStream) Close() error {
	if s.closer == nil {
		return nil
	}
	return s.closer.Close()
}

// newScanner wraps a response body with a line-splitting bufio.Scanner sized generously above the
// default 64KiB token limit — a chat/generate chunk carrying a long tool-call payload could
// otherwise trip bufio.ErrTooLong, which the default js/python NDJSON readers (unbounded string
// buffering) never hit.
func newScanner(body io.Reader) *bufio.Scanner {
	scanner := bufio.NewScanner(body)
	buf := make([]byte, 0, 64*1024)
	scanner.Buffer(buf, 8*1024*1024)
	return scanner
}
