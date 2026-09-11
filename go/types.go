package inferhub

import (
	"encoding/json"
	"net/http"
)

// Typed request/response shapes for the Ollama-dialect core surface (chat, generate, embeddings,
// model listing, status, health) — v0.1.0's scope only; retrieval and modalities land in
// v0.2.0/v1.0.0 (see plans/phase-22-go-core.md).
//
// Plain structs, not a code-generated schema: the wire is small and stable, and a validation
// library would be a dependency every consumer of this package inherits (root CLAUDE.md rule 2).
// Every response type carries an Extra map[string]any for fields the hub sends that this version
// does not know about yet — the same escape hatch python's `extra` dict, js's `extra` record and
// the C# client's [JsonExtensionData] give theirs. Request types carry an Extra field too, merged
// into the top-level JSON body on the way out.

// JSONDict is a loosely-typed JSON object — used for Ollama options/format bags and for the
// escape-hatch Extra fields, where a fixed Go struct would have to chase the hub's schema release
// for release.
type JSONDict = map[string]any

// ChatMessage is one message in a Chat/ChatStream request or response.
type ChatMessage struct {
	Role      string           `json:"role"`
	Content   string           `json:"content"`
	Images    []string         `json:"images,omitempty"`
	ToolCalls []JSONDict       `json:"tool_calls,omitempty"`
	// Extra holds fields this version does not know about — merged in on the way out (response),
	// kept apart on the way in (request), same as ChatRequest.Extra.
	Extra JSONDict `json:"-"`
}

func (m ChatMessage) MarshalJSON() ([]byte, error) {
	body := JSONDict{"role": m.Role, "content": m.Content}
	if m.Images != nil {
		body["images"] = m.Images
	}
	if m.ToolCalls != nil {
		body["tool_calls"] = m.ToolCalls
	}
	for k, v := range m.Extra {
		body[k] = v
	}
	return json.Marshal(body)
}

func (m *ChatMessage) UnmarshalJSON(data []byte) error {
	var raw JSONDict
	if err := json.Unmarshal(data, &raw); err != nil {
		return err
	}
	type alias struct {
		Role      string     `json:"role"`
		Content   string     `json:"content"`
		Images    []string   `json:"images"`
		ToolCalls []JSONDict `json:"tool_calls"`
	}
	var a alias
	if err := json.Unmarshal(data, &a); err != nil {
		return err
	}
	m.Role = a.Role
	m.Content = a.Content
	m.Images = a.Images
	m.ToolCalls = a.ToolCalls
	for _, key := range []string{"role", "content", "images", "tool_calls"} {
		delete(raw, key)
	}
	m.Extra = raw
	return nil
}

// ChatRequest is the body of POST /api/chat. Stream is set by Chat/ChatStream — not meant to be
// set by the caller directly.
type ChatRequest struct {
	Model    string        `json:"model"`
	Messages []ChatMessage `json:"messages"`
	Options  JSONDict      `json:"options,omitempty"`
	// Format is a string ("json") or a JSON schema object — either is valid on the wire.
	Format    any    `json:"format,omitempty"`
	KeepAlive string `json:"keep_alive,omitempty"`
	// Extra merges straight into the top-level request body — every Ollama option this client does
	// not type (tool definitions, future fields) stays reachable without a new release (non-goal in
	// the phase-22 brief, same as C#, Python and TypeScript).
	Extra JSONDict `json:"-"`
}

func (r ChatRequest) marshalWithStream(stream bool) ([]byte, error) {
	body := JSONDict{"model": r.Model, "messages": r.Messages, "stream": stream}
	if r.Options != nil {
		body["options"] = r.Options
	}
	if r.Format != nil {
		body["format"] = r.Format
	}
	if r.KeepAlive != "" {
		body["keep_alive"] = r.KeepAlive
	}
	for k, v := range r.Extra {
		body[k] = v
	}
	return json.Marshal(body)
}

// ChatResponse is the body of a (possibly partial, when streamed) response from POST /api/chat.
type ChatResponse struct {
	Model              string       `json:"model"`
	CreatedAt          *string      `json:"created_at,omitempty"`
	Message            *ChatMessage `json:"message,omitempty"`
	Done               *bool        `json:"done,omitempty"`
	DoneReason         *string      `json:"done_reason,omitempty"`
	TotalDuration      *int64       `json:"total_duration,omitempty"`
	LoadDuration       *int64       `json:"load_duration,omitempty"`
	PromptEvalCount    *int         `json:"prompt_eval_count,omitempty"`
	PromptEvalDuration *int64       `json:"prompt_eval_duration,omitempty"`
	EvalCount          *int         `json:"eval_count,omitempty"`
	EvalDuration       *int64       `json:"eval_duration,omitempty"`
	Error              *string      `json:"error,omitempty"`
	Extra              JSONDict     `json:"-"`

	// ServedBy and SourceIds are set from response headers, never the body — root CLAUDE.md rule 8:
	// surfaced, never interpreted. This client does not route or retry elsewhere on them.
	ServedBy  string   `json:"-"`
	SourceIDs []string `json:"-"`
}

var chatResponseKnownFields = []string{
	"model", "created_at", "message", "done", "done_reason", "total_duration", "load_duration",
	"prompt_eval_count", "prompt_eval_duration", "eval_count", "eval_duration", "error",
}

func (r *ChatResponse) UnmarshalJSON(data []byte) error {
	var raw JSONDict
	if err := json.Unmarshal(data, &raw); err != nil {
		return err
	}
	type alias struct {
		Model              string       `json:"model"`
		CreatedAt          *string      `json:"created_at"`
		Message            *ChatMessage `json:"message"`
		Done               *bool        `json:"done"`
		DoneReason         *string      `json:"done_reason"`
		TotalDuration      *int64       `json:"total_duration"`
		LoadDuration       *int64       `json:"load_duration"`
		PromptEvalCount    *int         `json:"prompt_eval_count"`
		PromptEvalDuration *int64       `json:"prompt_eval_duration"`
		EvalCount          *int         `json:"eval_count"`
		EvalDuration       *int64       `json:"eval_duration"`
		Error              *string      `json:"error"`
	}
	var a alias
	if err := json.Unmarshal(data, &a); err != nil {
		return err
	}
	r.Model = a.Model
	r.CreatedAt = a.CreatedAt
	r.Message = a.Message
	r.Done = a.Done
	r.DoneReason = a.DoneReason
	r.TotalDuration = a.TotalDuration
	r.LoadDuration = a.LoadDuration
	r.PromptEvalCount = a.PromptEvalCount
	r.PromptEvalDuration = a.PromptEvalDuration
	r.EvalCount = a.EvalCount
	r.EvalDuration = a.EvalDuration
	r.Error = a.Error
	for _, key := range chatResponseKnownFields {
		delete(raw, key)
	}
	r.Extra = raw
	return nil
}

// GenerateRequest is the body of POST /api/generate.
type GenerateRequest struct {
	Model     string   `json:"model"`
	Prompt    string   `json:"prompt"`
	Options   JSONDict `json:"options,omitempty"`
	Format    any      `json:"format,omitempty"`
	KeepAlive string   `json:"keep_alive,omitempty"`
	Extra     JSONDict `json:"-"`
}

func (r GenerateRequest) marshalWithStream(stream bool) ([]byte, error) {
	body := JSONDict{"model": r.Model, "prompt": r.Prompt, "stream": stream}
	if r.Options != nil {
		body["options"] = r.Options
	}
	if r.Format != nil {
		body["format"] = r.Format
	}
	if r.KeepAlive != "" {
		body["keep_alive"] = r.KeepAlive
	}
	for k, v := range r.Extra {
		body[k] = v
	}
	return json.Marshal(body)
}

// GenerateResponse is the body of a (possibly partial, when streamed) response from
// POST /api/generate.
type GenerateResponse struct {
	Model              string   `json:"model"`
	CreatedAt          *string  `json:"created_at,omitempty"`
	Response           string   `json:"response"`
	Done               *bool    `json:"done,omitempty"`
	DoneReason         *string  `json:"done_reason,omitempty"`
	Context            []int    `json:"context,omitempty"`
	TotalDuration      *int64   `json:"total_duration,omitempty"`
	LoadDuration       *int64   `json:"load_duration,omitempty"`
	PromptEvalCount    *int     `json:"prompt_eval_count,omitempty"`
	PromptEvalDuration *int64   `json:"prompt_eval_duration,omitempty"`
	EvalCount          *int     `json:"eval_count,omitempty"`
	EvalDuration       *int64   `json:"eval_duration,omitempty"`
	Error              *string  `json:"error,omitempty"`
	Extra              JSONDict `json:"-"`

	ServedBy  string   `json:"-"`
	SourceIDs []string `json:"-"`
}

var generateResponseKnownFields = []string{
	"model", "created_at", "response", "done", "done_reason", "context", "total_duration",
	"load_duration", "prompt_eval_count", "prompt_eval_duration", "eval_count", "eval_duration",
	"error",
}

func (r *GenerateResponse) UnmarshalJSON(data []byte) error {
	var raw JSONDict
	if err := json.Unmarshal(data, &raw); err != nil {
		return err
	}
	type alias struct {
		Model              string  `json:"model"`
		CreatedAt          *string `json:"created_at"`
		Response           string  `json:"response"`
		Done               *bool   `json:"done"`
		DoneReason         *string `json:"done_reason"`
		Context            []int   `json:"context"`
		TotalDuration      *int64  `json:"total_duration"`
		LoadDuration       *int64  `json:"load_duration"`
		PromptEvalCount    *int    `json:"prompt_eval_count"`
		PromptEvalDuration *int64  `json:"prompt_eval_duration"`
		EvalCount          *int    `json:"eval_count"`
		EvalDuration       *int64  `json:"eval_duration"`
		Error              *string `json:"error"`
	}
	var a alias
	if err := json.Unmarshal(data, &a); err != nil {
		return err
	}
	r.Model = a.Model
	r.CreatedAt = a.CreatedAt
	r.Response = a.Response
	r.Done = a.Done
	r.DoneReason = a.DoneReason
	r.Context = a.Context
	r.TotalDuration = a.TotalDuration
	r.LoadDuration = a.LoadDuration
	r.PromptEvalCount = a.PromptEvalCount
	r.PromptEvalDuration = a.PromptEvalDuration
	r.EvalCount = a.EvalCount
	r.EvalDuration = a.EvalDuration
	r.Error = a.Error
	for _, key := range generateResponseKnownFields {
		delete(raw, key)
	}
	r.Extra = raw
	return nil
}

// EmbedRequest is the body of POST /api/embed — the batch embeddings endpoint. Input is a string
// or a []string; Go has no union type, so this mirrors what the wire actually accepts rather than
// forcing a caller through two methods.
type EmbedRequest struct {
	Model string `json:"model"`
	Input any    `json:"input"`
}

// EmbedResponse is the body of a successful POST /api/embed response.
type EmbedResponse struct {
	Model      string      `json:"model"`
	Embeddings [][]float64 `json:"embeddings"`
}

// EmbeddingsRequest is the body of POST /api/embeddings — the legacy single-input endpoint.
// Prefer EmbedRequest / Client.Embed.
type EmbeddingsRequest struct {
	Model  string `json:"model"`
	Prompt string `json:"prompt"`
}

// EmbeddingsResponse is the body of a successful POST /api/embeddings response.
type EmbeddingsResponse struct {
	Embedding []float64 `json:"embedding"`
}

// ModelInfo describes one model the mesh advertises.
type ModelInfo struct {
	Name   string  `json:"name"`
	Digest *string `json:"digest,omitempty"`
	Size   *int64  `json:"size,omitempty"`
}

// TagsResponse is the body of GET /api/tags.
type TagsResponse struct {
	Models []ModelInfo `json:"models"`
}

// StatusResponse is the body of GET /api/status on a coordinator. Note: unlike chat/generate,
// which are snake_case on the wire (Ollama's own dialect), /api/status is camelCase — verified
// against conformance/cases.json's "hub-status-has-no-mode-field" case
// ({"coordinatorVersion":...,"nowUtc":...,"nodes":[{"nodeId":...}]}). Json tags below match the
// wire, not the repo-wide snake_case convention, on purpose.
type StatusResponse struct {
	CoordinatorVersion *string     `json:"coordinatorVersion,omitempty"`
	NowUTC             *string     `json:"nowUtc,omitempty"`
	UptimeSeconds      *float64    `json:"uptimeSeconds,omitempty"`
	Nodes              []JSONDict  `json:"nodes,omitempty"`
	Models             []ModelInfo `json:"-"`
	Extra              JSONDict    `json:"-"`
}

// statusResponseKnownFields excludes "metrics" and "vector" from Extra too (known-but-unmodeled,
// same carve-out js's statusResponseFromJson takes) even though this version does not type them.
var statusResponseKnownFields = []string{
	"coordinatorVersion", "nowUtc", "uptimeSeconds", "nodes", "models", "metrics", "vector",
}

func (r *StatusResponse) UnmarshalJSON(data []byte) error {
	var raw JSONDict
	if err := json.Unmarshal(data, &raw); err != nil {
		return err
	}
	type alias struct {
		CoordinatorVersion *string     `json:"coordinatorVersion"`
		NowUTC             *string     `json:"nowUtc"`
		UptimeSeconds      *float64    `json:"uptimeSeconds"`
		Nodes              []JSONDict  `json:"nodes"`
		Models             []ModelInfo `json:"models"`
	}
	var a alias
	if err := json.Unmarshal(data, &a); err != nil {
		return err
	}
	r.CoordinatorVersion = a.CoordinatorVersion
	r.NowUTC = a.NowUTC
	r.UptimeSeconds = a.UptimeSeconds
	r.Nodes = a.Nodes
	r.Models = a.Models
	for _, key := range statusResponseKnownFields {
		delete(raw, key)
	}
	r.Extra = raw
	return nil
}

// ClientOptions configures a new Client.
type ClientOptions struct {
	// BaseURL defaults to http://localhost:5080/ when empty.
	BaseURL string
	// APIKey, when set, is sent as "Authorization: Bearer <APIKey>" on every request.
	APIKey string
	// HTTPClient overrides the transport — for tests, custom timeouts/proxies/TLS, or reusing a
	// caller's own connection pool. Defaults to http.DefaultClient's shape (a fresh *http.Client
	// with no timeout override) when nil.
	HTTPClient *http.Client
}
