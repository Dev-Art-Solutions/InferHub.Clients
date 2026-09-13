package inferhub

// Unit tests for retrieval.go — the X-InferHub-Retrieve*/X-InferHub-Rerank headers, and that the
// variadic trailing parameter stays additive over a plain v0.1.0-shaped call (D1 in
// plans/phase-23-go-retrieval.md).

import (
	"context"
	"net/http"
	"testing"
)

func TestChatOmitsRetrievalHeadersWhenNoneGiven(t *testing.T) {
	var seenRetrieve bool
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("X-InferHub-Retrieve") != "" {
			seenRetrieve = true
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"model":"llama3","message":{"role":"assistant","content":"hi"}}`))
	})
	defer closeSrv()

	if _, err := client.Chat(context.Background(), ChatRequest{Model: "llama3"}); err != nil {
		t.Fatalf("Chat: %v", err)
	}
	if seenRetrieve {
		t.Errorf("X-InferHub-Retrieve sent for a plain call with no RetrievalOptions")
	}
}

func TestChatSendsAllFiveRetrievalHeaders(t *testing.T) {
	headers := map[string]string{}
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		for _, h := range []string{
			"X-InferHub-Retrieve", "X-InferHub-Retrieve-K", "X-InferHub-Retrieve-Model",
			"X-InferHub-Retrieve-Mode", "X-InferHub-Rerank",
		} {
			headers[h] = r.Header.Get(h)
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"model":"llama3","message":{"role":"assistant","content":"hi"}}`))
	})
	defer closeSrv()

	k := 5
	rerank := true
	_, err := client.Chat(context.Background(), ChatRequest{Model: "llama3"}, RetrievalOptions{
		Collection: "docs", K: &k, Model: "bge-small", Mode: "hybrid", Rerank: &rerank,
	})
	if err != nil {
		t.Fatalf("Chat: %v", err)
	}
	want := map[string]string{
		"X-InferHub-Retrieve":       "docs",
		"X-InferHub-Retrieve-K":     "5",
		"X-InferHub-Retrieve-Model": "bge-small",
		"X-InferHub-Retrieve-Mode":  "hybrid",
		"X-InferHub-Rerank":         "true",
	}
	for h, w := range want {
		if headers[h] != w {
			t.Errorf("%s = %q, want %q", h, headers[h], w)
		}
	}
}

func TestGenerateSendsRetrievalHeaders(t *testing.T) {
	var gotCollection string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotCollection = r.Header.Get("X-InferHub-Retrieve")
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"model":"llama3","response":"hi"}`))
	})
	defer closeSrv()

	_, err := client.Generate(context.Background(), GenerateRequest{Model: "llama3", Prompt: "hi"},
		RetrievalOptions{Collection: "docs"})
	if err != nil {
		t.Fatalf("Generate: %v", err)
	}
	if gotCollection != "docs" {
		t.Errorf("X-InferHub-Retrieve = %q, want docs", gotCollection)
	}
}
