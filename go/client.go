// Package inferhub is a client for InferHub, a self-hosted, Ollama-compatible inference mesh.
//
// This is the core surface (v0.1.0): chat, generate (blocking and streaming), embeddings, model
// listing, status/health, auth, the error model. Retrieval (v0.2.0) and modalities/admin/node
// (v1.0.0) are later phases — see plans/roadmap-polyglot-clients.md D3.
//
// Stdlib net/http only (root CLAUDE.md rule 2's Go budget): zero entries in go.mod's require block.
// Every method takes a context.Context as its first argument; errors are values, not panics — a
// non-success HTTP status comes back as *Error, with a Kind field (KindPlain/KindRetrieval/
// KindOpenAI) saying which envelope produced it — see errors.go.
package inferhub

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"strings"
)

// DefaultBaseURL is used when ClientOptions.BaseURL is empty.
const DefaultBaseURL = "http://localhost:5080/"

// Client talks to one InferHub coordinator or solo node — a node is a base address, not a second
// client type (root CLAUDE.md rule 6; roadmap D7): pointing this client at a node's address is the
// whole of "run it against a node".
type Client struct {
	baseURL    *url.URL
	apiKey     string
	httpClient *http.Client
}

// NewClient constructs a Client. A zero ClientOptions is valid and points at DefaultBaseURL with
// no auth and http.DefaultClient's shape.
func NewClient(opts ClientOptions) (*Client, error) {
	raw := opts.BaseURL
	if raw == "" {
		raw = DefaultBaseURL
	}
	if !strings.HasSuffix(raw, "/") {
		raw += "/"
	}
	base, err := url.Parse(raw)
	if err != nil {
		return nil, fmt.Errorf("inferhub: invalid base URL %q: %w", raw, err)
	}
	httpClient := opts.HTTPClient
	if httpClient == nil {
		httpClient = &http.Client{}
	}
	return &Client{baseURL: base, apiKey: opts.APIKey, httpClient: httpClient}, nil
}

func (c *Client) resolve(path string) (string, error) {
	ref, err := url.Parse(path)
	if err != nil {
		return "", err
	}
	return c.baseURL.ResolveReference(ref).String(), nil
}

// No default Content-Type at the client level (ported from js/python _base's finding): a
// client-level default header would win over what a per-request Content-Type should be, which
// would silently break a future multipart call. Every JSON-bodied method here sets it per request
// instead.
func (c *Client) buildHeaders() http.Header {
	h := http.Header{}
	if c.apiKey != "" {
		h.Set("Authorization", "Bearer "+c.apiKey)
	}
	return h
}

func (c *Client) newRequest(ctx context.Context, method, path string, body []byte) (*http.Request, error) {
	target, err := c.resolve(path)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	var reader io.Reader
	if body != nil {
		reader = bytes.NewReader(body)
	}
	req, err := http.NewRequestWithContext(ctx, method, target, reader)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	for k, v := range c.buildHeaders() {
		req.Header[k] = v
	}
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	return req, nil
}

// -- Error mapping ---------------------------------------------------------------------------------
// Which envelope arrived decides the exception type, never which method was called (root CLAUDE.md
// rule 9): /api/* answers {"error":"..."}; /v1/* and routes that reuse its shape answer
// {"error":{"message":...,"type":...,"param":...,"code":...}}. A 424 is always Kind == KindRetrieval,
// in either dialect. v0.1.0 has no /v1/* methods yet (non-goal), but the corpus's "424-is-not-404" and
// "two-dialects-two-envelopes" cases exercise this mapping directly, so it is implemented in full
// now rather than half now and half in a later phase that has to reopen this file.

// extractErrorMessage mirrors python's _extract_error_message / js's extractErrorMessage: the
// Ollama dialect answers {"error": "..."}. A non-JSON or differently-shaped body falls back to the
// raw text.
func extractErrorMessage(body string) string {
	trimmed := strings.TrimSpace(body)
	if trimmed == "" {
		return ""
	}
	var envelope struct {
		Error string `json:"error"`
	}
	if err := json.Unmarshal([]byte(trimmed), &envelope); err != nil {
		return body
	}
	if envelope.Error != "" {
		return envelope.Error
	}
	return body
}

type openAIEnvelope struct {
	Message string
	Code    string
	Param   string
	Type    string
}

// parseOpenAIEnvelope returns (envelope, true) for {"error":{"message":...}}, or (zero, false) when
// the body is not that shape, so the caller falls back to the Ollama dialect's plain-string
// envelope.
func parseOpenAIEnvelope(body string) (openAIEnvelope, bool) {
	var wire struct {
		Error *struct {
			Message string `json:"message"`
			Type    string `json:"type"`
			Param   string `json:"param"`
			Code    string `json:"code"`
		} `json:"error"`
	}
	if err := json.Unmarshal([]byte(body), &wire); err != nil || wire.Error == nil {
		return openAIEnvelope{}, false
	}
	msg := wire.Error.Message
	if msg == "" {
		msg = body
	}
	return openAIEnvelope{Message: msg, Code: wire.Error.Code, Param: wire.Error.Param, Type: wire.Error.Type}, true
}

func retryAfterFrom(resp *http.Response) *float64 {
	header := resp.Header.Get("Retry-After")
	if header == "" {
		return nil
	}
	// An HTTP-date form exists but the hub always writes delta-seconds, same carve-out
	// python's _retry_after and js's retryAfter take.
	value, err := strconv.ParseFloat(header, 64)
	if err != nil {
		return nil
	}
	return &value
}

// raiseForStatus reads and closes resp.Body on a non-2xx status and returns the typed error;
// callers that get a non-nil error must not read resp.Body again.
func raiseForStatus(resp *http.Response) error {
	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		return nil
	}
	bodyBytes, _ := io.ReadAll(resp.Body)
	resp.Body.Close()
	body := string(bodyBytes)
	retry := retryAfterFrom(resp)

	if resp.StatusCode == http.StatusFailedDependency { // 424
		message := extractErrorMessage(body)
		if message == "" {
			message = fmt.Sprintf("InferHub request failed with status %d.", resp.StatusCode)
		}
		return newRetrievalError(resp.StatusCode, message, body, retry)
	}

	if envelope, ok := parseOpenAIEnvelope(body); ok {
		return newOpenAIError(resp.StatusCode, envelope.Message, body, retry, envelope.Code, envelope.Param, envelope.Type)
	}

	message := extractErrorMessage(body)
	if message == "" {
		message = fmt.Sprintf("InferHub request failed with status %d.", resp.StatusCode)
	}
	return newError(resp.StatusCode, message, body, retry)
}

// -- NDJSON chunk parsing --------------------------------------------------------------------------

// parseNDJSONLine returns (raw, true, nil) for a chunk line to decode further, (_, false, nil) for
// a blank line to skip, or (_, _, err) — an *Error — when the line is a terminal error chunk
// ({"error": ..., "done": true}), so a caller's loop stops with a clear error instead of hanging or
// silently finishing early (root CLAUDE.md testing-discipline rule; conformance case
// "mid-stream-error-terminates-not-hangs").
func parseNDJSONLine(line string) (raw string, ok bool, err error) {
	trimmed := strings.TrimSpace(line)
	if trimmed == "" {
		return "", false, nil
	}
	var probe struct {
		Error string `json:"error"`
	}
	if unmarshalErr := json.Unmarshal([]byte(trimmed), &probe); unmarshalErr != nil {
		return "", false, fmt.Errorf("inferhub: malformed NDJSON line: %w", unmarshalErr)
	}
	if probe.Error != "" {
		return "", false, newError(200, probe.Error, trimmed, nil)
	}
	return trimmed, true, nil
}

func chatResponseFromJSON(raw string) (ChatResponse, error) {
	var resp ChatResponse
	err := json.Unmarshal([]byte(raw), &resp)
	return resp, err
}

func generateResponseFromJSON(raw string) (GenerateResponse, error) {
	var resp GenerateResponse
	err := json.Unmarshal([]byte(raw), &resp)
	return resp, err
}

// -- Headers surfaced, never interpreted (root CLAUDE.md rule 8) ------------------------------------

func readServedBy(resp *http.Response) string {
	return strings.TrimSpace(resp.Header.Get("X-InferHub-Served-By"))
}

// readSourceIDs parses X-InferHub-Sources, which arrives as a JSON array most of the time but has
// also been sent comma-separated by a real hub (spec/README.md calls this the conformance corpus's
// first case; conformance/cases.json's "sources-header-comma-fallback"). Both shapes are handled
// even though v0.1.0 has no way yet to opt into retrieval — the header rides on plain chat/generate
// responses regardless.
func readSourceIDs(resp *http.Response) []string {
	if !headerPresent(resp.Header, "X-InferHub-Sources") {
		return nil
	}
	raw := resp.Header.Get("X-InferHub-Sources")
	trimmed := strings.TrimSpace(raw)
	if trimmed == "" {
		return []string{}
	}
	var parsed []any
	if err := json.Unmarshal([]byte(trimmed), &parsed); err == nil {
		ids := make([]string, 0, len(parsed))
		for _, item := range parsed {
			if item == nil {
				continue
			}
			s := fmt.Sprintf("%v", item)
			if s != "" {
				ids = append(ids, s)
			}
		}
		return ids
	}
	parts := strings.Split(trimmed, ",")
	ids := make([]string, 0, len(parts))
	for _, part := range parts {
		p := strings.TrimSpace(part)
		if p != "" {
			ids = append(ids, p)
		}
	}
	return ids
}

func headerPresent(h http.Header, key string) bool {
	_, ok := h[http.CanonicalHeaderKey(key)]
	return ok
}

// -- Methods ----------------------------------------------------------------------------------------

// ListModels is GET /api/tags — models advertised by the mesh.
func (c *Client) ListModels(ctx context.Context) (TagsResponse, error) {
	var out TagsResponse
	req, err := c.newRequest(ctx, http.MethodGet, "api/tags", nil)
	if err != nil {
		return out, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return out, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding /api/tags response: %w", err)
	}
	return out, nil
}

// Chat is blocking chat — POST /api/chat with stream:false. A 424 returns *Error with
// Kind == KindRetrieval.
func (c *Client) Chat(ctx context.Context, request ChatRequest) (ChatResponse, error) {
	var out ChatResponse
	body, err := request.marshalWithStream(false)
	if err != nil {
		return out, fmt.Errorf("inferhub: encoding chat request: %w", err)
	}
	req, err := c.newRequest(ctx, http.MethodPost, "api/chat", body)
	if err != nil {
		return out, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return out, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding /api/chat response: %w", err)
	}
	out.ServedBy = readServedBy(resp)
	out.SourceIDs = readSourceIDs(resp)
	return out, nil
}

// ChatStream is streaming chat — POST /api/chat with stream:true. Iterate with stream.Next(); a
// terminal error chunk surfaces from Next/Err instead of the loop hanging or ending quietly. The
// caller must Close the returned *ChatStream once done (a deferred Close after a nil error is the
// normal shape); when ChatStream itself returns a non-nil error, no stream is returned and there is
// nothing to close.
func (c *Client) ChatStream(ctx context.Context, request ChatRequest) (*ChatStream, error) {
	body, err := request.marshalWithStream(true)
	if err != nil {
		return nil, fmt.Errorf("inferhub: encoding chat request: %w", err)
	}
	req, err := c.newRequest(ctx, http.MethodPost, "api/chat", body)
	if err != nil {
		return nil, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	if err := raiseForStatus(resp); err != nil {
		return nil, err
	}
	return &ChatStream{
		scanner:   newScanner(resp.Body),
		closer:    resp.Body,
		servedBy:  readServedBy(resp),
		sourceIDs: readSourceIDs(resp),
	}, nil
}

// Generate is blocking generate — POST /api/generate with stream:false.
func (c *Client) Generate(ctx context.Context, request GenerateRequest) (GenerateResponse, error) {
	var out GenerateResponse
	body, err := request.marshalWithStream(false)
	if err != nil {
		return out, fmt.Errorf("inferhub: encoding generate request: %w", err)
	}
	req, err := c.newRequest(ctx, http.MethodPost, "api/generate", body)
	if err != nil {
		return out, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return out, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding /api/generate response: %w", err)
	}
	out.ServedBy = readServedBy(resp)
	out.SourceIDs = readSourceIDs(resp)
	return out, nil
}

// GenerateStream is streaming generate — POST /api/generate with stream:true.
func (c *Client) GenerateStream(ctx context.Context, request GenerateRequest) (*GenerateStream, error) {
	body, err := request.marshalWithStream(true)
	if err != nil {
		return nil, fmt.Errorf("inferhub: encoding generate request: %w", err)
	}
	req, err := c.newRequest(ctx, http.MethodPost, "api/generate", body)
	if err != nil {
		return nil, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	if err := raiseForStatus(resp); err != nil {
		return nil, err
	}
	return &GenerateStream{
		scanner:   newScanner(resp.Body),
		closer:    resp.Body,
		servedBy:  readServedBy(resp),
		sourceIDs: readSourceIDs(resp),
	}, nil
}

// Embed is POST /api/embed — batch embeddings. An empty vector list on a 200 is treated as a
// malformed response and returned as an error, never silently handed back.
func (c *Client) Embed(ctx context.Context, request EmbedRequest) (EmbedResponse, error) {
	var out EmbedResponse
	body, err := json.Marshal(request)
	if err != nil {
		return out, fmt.Errorf("inferhub: encoding embed request: %w", err)
	}
	req, err := c.newRequest(ctx, http.MethodPost, "api/embed", body)
	if err != nil {
		return out, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return out, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding /api/embed response: %w", err)
	}
	if len(out.Embeddings) == 0 {
		return out, newError(http.StatusOK, "embed response had no vectors", "", nil)
	}
	return out, nil
}

// EmbedLegacy is POST /api/embeddings — the legacy single-input endpoint. Prefer Embed.
func (c *Client) EmbedLegacy(ctx context.Context, request EmbeddingsRequest) (EmbeddingsResponse, error) {
	var out EmbeddingsResponse
	body, err := json.Marshal(request)
	if err != nil {
		return out, fmt.Errorf("inferhub: encoding embeddings request: %w", err)
	}
	req, err := c.newRequest(ctx, http.MethodPost, "api/embeddings", body)
	if err != nil {
		return out, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return out, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding /api/embeddings response: %w", err)
	}
	if len(out.Embedding) == 0 {
		return out, newError(http.StatusOK, "embeddings response had no vector", "", nil)
	}
	return out, nil
}

// Status is GET /api/status — coordinator/fleet snapshot (or a solo node's own status; probe()-style
// mode discrimination is a later phase — root CLAUDE.md rule 6 non-goal note in
// plans/phase-22-go-core.md).
func (c *Client) Status(ctx context.Context) (StatusResponse, error) {
	var out StatusResponse
	req, err := c.newRequest(ctx, http.MethodGet, "api/status", nil)
	if err != nil {
		return out, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return out, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding /api/status response: %w", err)
	}
	return out, nil
}

// Ping is GET /health — true on 2xx, false otherwise. Never returns an error for a non-success
// status; only a transport error is returned as one.
func (c *Client) Ping(ctx context.Context) (bool, error) {
	req, err := c.newRequest(ctx, http.MethodGet, "health", nil)
	if err != nil {
		return false, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return false, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	return resp.StatusCode >= 200 && resp.StatusCode < 300, nil
}
