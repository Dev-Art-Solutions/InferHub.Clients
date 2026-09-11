package inferhub

// Phase 22 — drives conformance/cases.json against Client, the same file the C#, Python and
// TypeScript runners read (conformance/README.md). A case whose `kind` is outside go/v0.1.0's
// surface is skipped with t.Skip and a named reason rather than silently omitted — mirrors js
// test/conformance.test.ts's SUPPORTED_KINDS split (4 covered, 9 skipped) exactly, since go/v0.1.0
// covers the identical surface (chat + chat-stream; no probe(), no OpenAI dialect, no retrieval).

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

type conformanceCase struct {
	ID          string `json:"id"`
	Kind        string `json:"kind"`
	Description string `json:"description"`
	Request     struct {
		Method string `json:"method"`
		Path   string `json:"path"`
	} `json:"request"`
	Response struct {
		Status    int               `json:"status"`
		Headers   map[string]string `json:"headers"`
		MediaType string            `json:"mediaType"`
		Body      string            `json:"body"`
	} `json:"response"`
	Assert map[string]any `json:"assert"`
}

type conformanceFile struct {
	Cases []conformanceCase `json:"cases"`
}

// supportedKinds is go/v0.1.0's surface: chat + chat-stream only (no probe, no OpenAI dialect, no
// retrieval/ingestion/search/chunks — those land in go/v0.2.0 and go/v1.0.0, same split js/v0.1.0
// and python 16 already proved).
var supportedKinds = map[string]bool{"chat": true, "chat-stream": true}

// findCasesFile walks up from the test's working directory to find conformance/cases.json, the
// same "read the shared file directly, never a copy" approach js's findCasesFile and python's
// runner take.
func findCasesFile(t *testing.T) string {
	t.Helper()
	dir, err := os.Getwd()
	if err != nil {
		t.Fatalf("os.Getwd: %v", err)
	}
	for i := 0; i < 10; i++ {
		candidate := filepath.Join(dir, "conformance", "cases.json")
		if _, err := os.Stat(candidate); err == nil {
			return candidate
		}
		parent := filepath.Dir(dir)
		if parent == dir {
			break
		}
		dir = parent
	}
	t.Fatalf("conformance/cases.json not found above %s", dir)
	return ""
}

func loadConformanceCases(t *testing.T) []conformanceCase {
	t.Helper()
	path := findCasesFile(t)
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("reading %s: %v", path, err)
	}
	var file conformanceFile
	if err := json.Unmarshal(data, &file); err != nil {
		t.Fatalf("parsing %s: %v", path, err)
	}
	return file.Cases
}

func clientForCase(t *testing.T, tc conformanceCase) (*Client, func()) {
	t.Helper()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		mediaType := tc.Response.MediaType
		if mediaType == "" {
			mediaType = "application/json"
		}
		w.Header().Set("Content-Type", mediaType)
		for k, v := range tc.Response.Headers {
			w.Header().Set(k, v)
		}
		w.WriteHeader(tc.Response.Status)
		_, _ = w.Write([]byte(tc.Response.Body))
	}))
	client, err := NewClient(ClientOptions{BaseURL: srv.URL, HTTPClient: srv.Client()})
	if err != nil {
		t.Fatalf("NewClient: %v", err)
	}
	return client, srv.Close
}

func TestConformanceCorpus(t *testing.T) {
	cases := loadConformanceCases(t)
	for _, tc := range cases {
		tc := tc
		t.Run(tc.ID, func(t *testing.T) {
			if !supportedKinds[tc.Kind] {
				t.Skipf("%q is outside inferhub go client v0.1.0's surface (core: chat/generate/embed/status "+
					"only — retrieval, the OpenAI dialect, ingestion/search/chunks land in go/v0.2.0 and go/v1.0.0)", tc.Kind)
			}

			assertKind, _ := tc.Assert["kind"].(string)
			client, closeSrv := clientForCase(t, tc)
			defer closeSrv()
			req := ChatRequest{Model: "llama3"}

			switch assertKind {
			case "throws-retrieval-exception":
				_, err := client.Chat(context.Background(), req)
				var inferErr *Error
				if !errors.As(err, &inferErr) {
					t.Fatalf("Chat error = %v, want *Error", err)
				}
				if !inferErr.IsRetrieval() {
					t.Errorf("Kind = %v, want KindRetrieval", inferErr.Kind)
				}
				if inferErr.StatusCode != 424 {
					t.Errorf("StatusCode = %d, want 424", inferErr.StatusCode)
				}

			case "source-ids":
				result, err := client.Chat(context.Background(), req)
				if err != nil {
					t.Fatalf("Chat: %v", err)
				}
				expected, _ := tc.Assert["expected"].([]any)
				if len(result.SourceIDs) != len(expected) {
					t.Fatalf("SourceIDs = %v, want %v", result.SourceIDs, expected)
				}
				for i, v := range expected {
					if result.SourceIDs[i] != v {
						t.Errorf("SourceIDs[%d] = %q, want %q", i, result.SourceIDs[i], v)
					}
				}

			case "stream-terminal-error":
				stream, err := client.ChatStream(context.Background(), req)
				if err != nil {
					t.Fatalf("ChatStream: %v", err)
				}
				defer stream.Close()
				var seen int
				for stream.Next() {
					seen++
				}
				var inferErr *Error
				if !errors.As(stream.Err(), &inferErr) {
					t.Fatalf("stream.Err() = %v, want *Error", stream.Err())
				}
				wantPartial, _ := tc.Assert["partialChunks"].(float64)
				if float64(seen) != wantPartial {
					t.Errorf("saw %d chunks before the terminal error, want %v", seen, wantPartial)
				}

			default:
				t.Fatalf("assert.kind %q has no runner for kind %q yet", assertKind, tc.Kind)
			}
		})
	}
}

// TestConformanceCorpusCoverage asserts the split stays 4 covered / 9 skipped (13 cases total, from
// phase 15) rather than silently drifting if the corpus grows and this runner's supportedKinds does
// not — same coverage assertion js's conformance.test.ts and python's test_conformance.py make.
func TestConformanceCorpusCoverage(t *testing.T) {
	cases := loadConformanceCases(t)
	var covered, skipped int
	for _, tc := range cases {
		if supportedKinds[tc.Kind] {
			covered++
		} else {
			skipped++
		}
	}
	if covered+skipped != len(cases) {
		t.Fatalf("covered(%d)+skipped(%d) != total(%d)", covered, skipped, len(cases))
	}
	if covered != 4 {
		t.Errorf("covered = %d, want 4", covered)
	}
	if skipped != 9 {
		t.Errorf("skipped = %d, want 9", skipped)
	}
}
