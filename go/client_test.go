package inferhub

// Unit tests against httptest.Server — no test in this repository calls a live hub (root CLAUDE.md
// testing-discipline rule). conformance_test.go drives the shared corpus; this file covers request
// shaping, streaming mechanics and error mapping the corpus does not, mirroring js/test/client.test.ts
// and python/tests/test_client.py.

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"
)

func newTestServer(t *testing.T, handler http.HandlerFunc) (*Client, func()) {
	t.Helper()
	srv := httptest.NewServer(handler)
	client, err := NewClient(ClientOptions{BaseURL: srv.URL, HTTPClient: srv.Client()})
	if err != nil {
		t.Fatalf("NewClient: %v", err)
	}
	return client, srv.Close
}

func TestAuthorizationHeaderSentWhenAPIKeyGiven(t *testing.T) {
	var gotAuth string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotAuth = r.Header.Get("Authorization")
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"models":[{"name":"llama3"}]}`))
	})
	defer closeSrv()
	client.apiKey = "sk-client-token-1"

	if _, err := client.ListModels(context.Background()); err != nil {
		t.Fatalf("ListModels: %v", err)
	}
	if gotAuth != "Bearer sk-client-token-1" {
		t.Errorf("Authorization = %q, want Bearer sk-client-token-1", gotAuth)
	}
}

func TestAuthorizationHeaderOmittedWhenNoAPIKey(t *testing.T) {
	var gotAuth string
	seenAuth := false
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotAuth = r.Header.Get("Authorization")
		seenAuth = true
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"models":[]}`))
	})
	defer closeSrv()

	if _, err := client.ListModels(context.Background()); err != nil {
		t.Fatalf("ListModels: %v", err)
	}
	if !seenAuth || gotAuth != "" {
		t.Errorf("Authorization = %q, want empty", gotAuth)
	}
}

func TestChatSendsStreamFalseAndParsesResponse(t *testing.T) {
	var gotPath, gotBody string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotPath = r.URL.Path
		b, _ := io.ReadAll(r.Body)
		gotBody = string(b)
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"model":"llama3","message":{"role":"assistant","content":"hi there"},"done":true,"eval_count":12}`))
	})
	defer closeSrv()

	result, err := client.Chat(context.Background(), ChatRequest{
		Model:    "llama3",
		Messages: []ChatMessage{{Role: "user", Content: "hi"}},
	})
	if err != nil {
		t.Fatalf("Chat: %v", err)
	}
	if gotPath != "/api/chat" {
		t.Errorf("path = %q, want /api/chat", gotPath)
	}
	var sent map[string]any
	if err := json.Unmarshal([]byte(gotBody), &sent); err != nil {
		t.Fatalf("unmarshal sent body: %v", err)
	}
	if sent["stream"] != false {
		t.Errorf("stream = %v, want false", sent["stream"])
	}
	if result.Message == nil || result.Message.Content != "hi there" {
		t.Errorf("message.content = %+v, want %q", result.Message, "hi there")
	}
	if result.EvalCount == nil || *result.EvalCount != 12 {
		t.Errorf("evalCount = %v, want 12", result.EvalCount)
	}
	if result.Done == nil || !*result.Done {
		t.Errorf("done = %v, want true", result.Done)
	}
}

func TestChatKeepsUnknownResponseFieldsInExtra(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true,"a_future_field":42}`))
	})
	defer closeSrv()

	result, err := client.Chat(context.Background(), ChatRequest{Model: "llama3"})
	if err != nil {
		t.Fatalf("Chat: %v", err)
	}
	if v, ok := result.Extra["a_future_field"]; !ok || v != float64(42) {
		t.Errorf("extra[a_future_field] = %v, ok=%v, want 42", v, ok)
	}
	if len(result.Extra) != 1 {
		t.Errorf("extra = %+v, want exactly one key", result.Extra)
	}
}

func TestChatMergesExtraAndPassesOptionsFormatKeepAlive(t *testing.T) {
	var gotBody string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		gotBody = string(b)
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"model":"llama3","message":{"role":"assistant","content":""},"done":true}`))
	})
	defer closeSrv()

	_, err := client.Chat(context.Background(), ChatRequest{
		Model:     "llama3",
		Options:   JSONDict{"temperature": 0.2},
		Format:    "json",
		KeepAlive: "5m",
		Extra:     JSONDict{"tools": []any{JSONDict{"type": "function"}}},
	})
	if err != nil {
		t.Fatalf("Chat: %v", err)
	}
	var sent map[string]any
	_ = json.Unmarshal([]byte(gotBody), &sent)
	if sent["format"] != "json" {
		t.Errorf("format = %v, want json", sent["format"])
	}
	if sent["keep_alive"] != "5m" {
		t.Errorf("keep_alive = %v, want 5m", sent["keep_alive"])
	}
	if _, ok := sent["tools"]; !ok {
		t.Errorf("tools missing from merged body: %v", sent)
	}
}

func TestGenerateSendsStreamFalseAndParsesResponse(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"model":"llama3","response":"hello","done":true,"context":[1,2,3]}`))
	})
	defer closeSrv()

	result, err := client.Generate(context.Background(), GenerateRequest{Model: "llama3", Prompt: "hi"})
	if err != nil {
		t.Fatalf("Generate: %v", err)
	}
	if result.Response != "hello" {
		t.Errorf("response = %q, want hello", result.Response)
	}
	if len(result.Context) != 3 {
		t.Errorf("context = %v, want 3 elements", result.Context)
	}
}

func ndjsonHandler(lines []string, extraHeaders map[string]string) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/x-ndjson")
		for k, v := range extraHeaders {
			w.Header().Set(k, v)
		}
		for _, line := range lines {
			_, _ = w.Write([]byte(line + "\n"))
		}
	}
}

func TestChatStreamYieldsOneChunkPerLineAndStopsAtDone(t *testing.T) {
	client, closeSrv := newTestServer(t, ndjsonHandler([]string{
		`{"model":"llama3","message":{"role":"assistant","content":"a"},"done":false}`,
		`{"model":"llama3","message":{"role":"assistant","content":"b"},"done":false}`,
		`{"model":"llama3","message":{"role":"assistant","content":""},"done":true}`,
	}, nil))
	defer closeSrv()

	stream, err := client.ChatStream(context.Background(), ChatRequest{Model: "llama3"})
	if err != nil {
		t.Fatalf("ChatStream: %v", err)
	}
	defer stream.Close()

	var chunks []ChatResponse
	for stream.Next() {
		chunks = append(chunks, stream.Value())
	}
	if err := stream.Err(); err != nil {
		t.Fatalf("stream.Err() = %v", err)
	}
	if len(chunks) != 3 {
		t.Fatalf("got %d chunks, want 3", len(chunks))
	}
	if chunks[0].Message.Content != "a" {
		t.Errorf("chunks[0].Message.Content = %q, want a", chunks[0].Message.Content)
	}
	if chunks[2].Done == nil || !*chunks[2].Done {
		t.Errorf("chunks[2].Done = %v, want true", chunks[2].Done)
	}
}

func TestChatStreamThrowsOnMidStreamTerminalError(t *testing.T) {
	client, closeSrv := newTestServer(t, ndjsonHandler([]string{
		`{"model":"llama3","message":{"role":"assistant","content":"partial"},"done":false}`,
		`{"error":"node dropped mid-stream","done":true}`,
	}, nil))
	defer closeSrv()

	stream, err := client.ChatStream(context.Background(), ChatRequest{Model: "llama3"})
	if err != nil {
		t.Fatalf("ChatStream: %v", err)
	}
	defer stream.Close()

	var seen int
	for stream.Next() {
		seen++
	}
	if seen != 1 {
		t.Errorf("saw %d chunks before the terminal error, want 1", seen)
	}
	var inferErr *Error
	if !errors.As(stream.Err(), &inferErr) {
		t.Fatalf("stream.Err() = %v, want *Error", stream.Err())
	}
	if inferErr.Kind != KindPlain {
		t.Errorf("Kind = %v, want KindPlain (a mid-stream error is not retrieval/openai-specific)", inferErr.Kind)
	}
}

func TestGenerateStreamYieldsChunksAndStopsAtDone(t *testing.T) {
	client, closeSrv := newTestServer(t, ndjsonHandler([]string{
		`{"model":"llama3","response":"a","done":false}`,
		`{"model":"llama3","response":"b","done":true}`,
	}, nil))
	defer closeSrv()

	stream, err := client.GenerateStream(context.Background(), GenerateRequest{Model: "llama3", Prompt: "hi"})
	if err != nil {
		t.Fatalf("GenerateStream: %v", err)
	}
	defer stream.Close()

	var chunks []GenerateResponse
	for stream.Next() {
		chunks = append(chunks, stream.Value())
	}
	if len(chunks) != 2 {
		t.Fatalf("got %d chunks, want 2", len(chunks))
	}
	if chunks[1].Done == nil || !*chunks[1].Done {
		t.Errorf("chunks[1].Done = %v, want true", chunks[1].Done)
	}
}

func TestChatReadsServedByAndSourcesJSONArray(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.Header().Set("X-InferHub-Served-By", "node-1")
		w.Header().Set("X-InferHub-Sources", `["doc-1","doc-2"]`)
		_, _ = w.Write([]byte(`{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true}`))
	})
	defer closeSrv()

	result, err := client.Chat(context.Background(), ChatRequest{Model: "llama3"})
	if err != nil {
		t.Fatalf("Chat: %v", err)
	}
	if result.ServedBy != "node-1" {
		t.Errorf("ServedBy = %q, want node-1", result.ServedBy)
	}
	if len(result.SourceIDs) != 2 || result.SourceIDs[0] != "doc-1" || result.SourceIDs[1] != "doc-2" {
		t.Errorf("SourceIDs = %v, want [doc-1 doc-2]", result.SourceIDs)
	}
}

func TestChatFallsBackToCommaSeparatedSources(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.Header().Set("X-InferHub-Sources", "doc-1,doc-2")
		_, _ = w.Write([]byte(`{"model":"llama3","message":{"role":"assistant","content":"hi"},"done":true}`))
	})
	defer closeSrv()

	result, err := client.Chat(context.Background(), ChatRequest{Model: "llama3"})
	if err != nil {
		t.Fatalf("Chat: %v", err)
	}
	if len(result.SourceIDs) != 2 || result.SourceIDs[0] != "doc-1" || result.SourceIDs[1] != "doc-2" {
		t.Errorf("SourceIDs = %v, want [doc-1 doc-2]", result.SourceIDs)
	}
}

func TestChatThrowsRetrievalErrorOn424(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusFailedDependency)
		_, _ = w.Write([]byte(`{"error":"retrieval unavailable"}`))
	})
	defer closeSrv()

	_, err := client.Chat(context.Background(), ChatRequest{Model: "llama3"})
	var inferErr *Error
	if !errors.As(err, &inferErr) {
		t.Fatalf("err = %v, want *Error", err)
	}
	if !inferErr.IsRetrieval() {
		t.Errorf("Kind = %v, want KindRetrieval", inferErr.Kind)
	}
	if inferErr.StatusCode != 424 {
		t.Errorf("StatusCode = %d, want 424", inferErr.StatusCode)
	}
}

func TestChatThrowsOpenAIErrorForNestedEnvelope(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusBadRequest)
		_, _ = w.Write([]byte(`{"error":{"message":"no such model","type":"invalid_request_error","param":"model","code":"model_not_found"}}`))
	})
	defer closeSrv()

	_, err := client.Chat(context.Background(), ChatRequest{Model: "llama3"})
	var inferErr *Error
	if !errors.As(err, &inferErr) {
		t.Fatalf("err = %v, want *Error", err)
	}
	if !inferErr.IsOpenAI() {
		t.Errorf("Kind = %v, want KindOpenAI", inferErr.Kind)
	}
	if inferErr.Code != "model_not_found" {
		t.Errorf("Code = %q, want model_not_found", inferErr.Code)
	}
	if inferErr.Param != "model" {
		t.Errorf("Param = %q, want model", inferErr.Param)
	}
}

func TestChatThrowsPlainErrorForStringEnvelope(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
		_, _ = w.Write([]byte(`{"error":"model 'x' not found"}`))
	})
	defer closeSrv()

	_, err := client.Chat(context.Background(), ChatRequest{Model: "x"})
	var inferErr *Error
	if !errors.As(err, &inferErr) {
		t.Fatalf("err = %v, want *Error", err)
	}
	if inferErr.Kind != KindPlain {
		t.Errorf("Kind = %v, want KindPlain (a bare {\"error\":\"...\"} string is not the OpenAI envelope)", inferErr.Kind)
	}
	if inferErr.StatusCode != 404 {
		t.Errorf("StatusCode = %d, want 404", inferErr.StatusCode)
	}
	if inferErr.Message != "model 'x' not found" {
		t.Errorf("Message = %q, want %q", inferErr.Message, "model 'x' not found")
	}
}

func TestChatCapturesRetryAfter(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Retry-After", "30")
		w.WriteHeader(http.StatusServiceUnavailable)
		_, _ = w.Write([]byte(`{"error":{"message":"busy","type":"api_error","param":null,"code":"capability_unavailable"}}`))
	})
	defer closeSrv()

	_, err := client.Chat(context.Background(), ChatRequest{Model: "llama3"})
	var base *Error
	if !errors.As(err, &base) {
		t.Fatalf("err = %v, want *Error", err)
	}
	if base.RetryAfter == nil || *base.RetryAfter != 30 {
		t.Errorf("RetryAfter = %v, want 30", base.RetryAfter)
	}
}

func TestEmbedReturnsParsedVectors(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"model":"nomic-embed-text","embeddings":[[0.1,0.2]]}`))
	})
	defer closeSrv()

	result, err := client.Embed(context.Background(), EmbedRequest{Model: "nomic-embed-text", Input: "hello"})
	if err != nil {
		t.Fatalf("Embed: %v", err)
	}
	if len(result.Embeddings) != 1 || len(result.Embeddings[0]) != 2 {
		t.Errorf("Embeddings = %v, want one vector of length 2", result.Embeddings)
	}
}

func TestEmbedThrowsOnEmptyVectorList(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"model":"nomic-embed-text","embeddings":[]}`))
	})
	defer closeSrv()

	_, err := client.Embed(context.Background(), EmbedRequest{Model: "nomic-embed-text", Input: "hello"})
	var base *Error
	if !errors.As(err, &base) {
		t.Fatalf("err = %v, want *Error", err)
	}
}

func TestEmbedLegacyReturnsParsedVector(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"embedding":[0.5,0.6]}`))
	})
	defer closeSrv()

	result, err := client.EmbedLegacy(context.Background(), EmbeddingsRequest{Model: "nomic-embed-text", Prompt: "hello"})
	if err != nil {
		t.Fatalf("EmbedLegacy: %v", err)
	}
	if len(result.Embedding) != 2 {
		t.Errorf("Embedding = %v, want length 2", result.Embedding)
	}
}

func TestStatusParsesHubShapeAndKeepsExtra(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"coordinatorVersion":"3.37.0","nowUtc":"2026-09-05T00:00:00Z","uptimeSeconds":12.5,"nodes":[{"nodeId":"n1","name":"gpu-1"}],"models":[{"name":"llama3"}],"somethingNew":true}`))
	})
	defer closeSrv()

	status, err := client.Status(context.Background())
	if err != nil {
		t.Fatalf("Status: %v", err)
	}
	if status.CoordinatorVersion == nil || *status.CoordinatorVersion != "3.37.0" {
		t.Errorf("CoordinatorVersion = %v, want 3.37.0", status.CoordinatorVersion)
	}
	if len(status.Nodes) != 1 {
		t.Errorf("Nodes = %v, want one entry", status.Nodes)
	}
	if len(status.Models) != 1 || status.Models[0].Name != "llama3" {
		t.Errorf("Models = %v, want [{llama3}]", status.Models)
	}
	if v, ok := status.Extra["somethingNew"]; !ok || v != true {
		t.Errorf("Extra[somethingNew] = %v, ok=%v, want true", v, ok)
	}
}

func TestPingReturnsTrueOn2xxFalseOtherwiseNeverErrors(t *testing.T) {
	okClient, closeOK := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
	})
	defer closeOK()
	ok, err := okClient.Ping(context.Background())
	if err != nil || !ok {
		t.Errorf("Ping = (%v, %v), want (true, nil)", ok, err)
	}

	downClient, closeDown := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusServiceUnavailable)
	})
	defer closeDown()
	down, err := downClient.Ping(context.Background())
	if err != nil || down {
		t.Errorf("Ping = (%v, %v), want (false, nil)", down, err)
	}
}

func TestListModelsParsesTagsResponse(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"models":[{"name":"llama3","digest":"abc","size":123}]}`))
	})
	defer closeSrv()

	result, err := client.ListModels(context.Background())
	if err != nil {
		t.Fatalf("ListModels: %v", err)
	}
	if len(result.Models) != 1 || result.Models[0].Name != "llama3" {
		t.Errorf("Models = %+v, want one llama3 entry", result.Models)
	}
	if result.Models[0].Digest == nil || *result.Models[0].Digest != "abc" {
		t.Errorf("Digest = %v, want abc", result.Models[0].Digest)
	}
}

func TestBaseURLResolvesRegardlessOfTrailingSlash(t *testing.T) {
	var gotPath string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotPath = r.URL.Path
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"models":[]}`))
	}))
	defer srv.Close()

	client, err := NewClient(ClientOptions{BaseURL: srv.URL, HTTPClient: srv.Client()}) // no trailing slash
	if err != nil {
		t.Fatalf("NewClient: %v", err)
	}
	if _, err := client.ListModels(context.Background()); err != nil {
		t.Fatalf("ListModels: %v", err)
	}
	if gotPath != "/api/tags" {
		t.Errorf("path = %q, want /api/tags", gotPath)
	}
}
