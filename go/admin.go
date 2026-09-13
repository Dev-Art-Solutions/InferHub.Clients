package inferhub

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"time"
)

// The admin plane (/api/admin/*, an admin key) and the node (a base address, not a second client —
// root rule 6) — go/v1.0.0. One file because both are "the rest of the surface a caller reaches
// with the same *Client", and because the node's own collection lifecycle (/api/collections) is
// deliberately not the admin one (dotnet D2/D3): keeping them apart in two files would suggest a
// relationship between the two that does not exist.

func extra(raw JSONDict, known ...string) JSONDict {
	out := JSONDict{}
	knownSet := make(map[string]bool, len(known))
	for _, k := range known {
		knownSet[k] = true
	}
	for k, v := range raw {
		if !knownSet[k] {
			out[k] = v
		}
	}
	return out
}

// AdminNode is one entry of ListNodes.
type AdminNode struct {
	NodeID string   `json:"nodeId"`
	Name   string   `json:"name,omitempty"`
	Extra  JSONDict `json:"-"`
}

func adminNodeFromJSON(raw JSONDict) AdminNode {
	n := AdminNode{Extra: extra(raw, "nodeId", "name")}
	if v, ok := raw["nodeId"].(string); ok {
		n.NodeID = v
	}
	if v, ok := raw["name"].(string); ok {
		n.Name = v
	}
	return n
}

// CollectionInfo describes one vector collection.
type CollectionInfo struct {
	Name        string   `json:"name"`
	Dimension   int      `json:"dimension"`
	Distance    string   `json:"distance,omitempty"`
	RecordCount *int64   `json:"recordCount,omitempty"`
	Operations  *int64   `json:"operations,omitempty"`
	Extra       JSONDict `json:"-"`
}

var collectionInfoKnown = []string{"name", "dimension", "distance", "recordCount", "operations"}

func (c *CollectionInfo) populateFrom(body []byte) error {
	if err := json.Unmarshal(body, c); err != nil {
		return err
	}
	c.Extra = extractExtra(body, collectionInfoKnown)
	return nil
}

// CollectionsResponse is GET /api/admin/vector/collections.
type CollectionsResponse struct {
	Collections []CollectionInfo `json:"collections"`
	Extra       JSONDict         `json:"-"`
}

// CollectionDetail is GET /api/admin/vector/collections/{name}.
type CollectionDetail struct {
	Name            string   `json:"name"`
	Dimension       int      `json:"dimension"`
	Distance        string   `json:"distance,omitempty"`
	UnderReplicated *bool    `json:"underReplicated,omitempty"`
	Extra           JSONDict `json:"-"`
}

var collectionDetailKnown = []string{"name", "dimension", "distance", "underReplicated"}

// NodeProfile is what the coordinator says a node should be running. One shape for both
// directions (dotnet D6) — Name/Revision are ignored on write, the hub sets both from the route
// and its own counter regardless of what is sent.
type NodeProfile struct {
	Name           string   `json:"name"`
	Revision       int      `json:"revision"`
	Selector       JSONDict `json:"selector"`
	Models         JSONDict `json:"models,omitempty"`
	MaxConcurrency *int     `json:"maxConcurrency,omitempty"`
	Retrieval      JSONDict `json:"retrieval,omitempty"`
	Extra          JSONDict `json:"-"`
}

var nodeProfileKnown = []string{"name", "revision", "selector", "models", "maxConcurrency", "retrieval"}

func (p NodeProfile) toJSON() JSONDict {
	body := JSONDict{"selector": p.Selector}
	if p.Models != nil {
		body["models"] = p.Models
	}
	if p.MaxConcurrency != nil {
		body["maxConcurrency"] = *p.MaxConcurrency
	}
	if p.Retrieval != nil {
		body["retrieval"] = p.Retrieval
	}
	return body
}

// PutProfileResult is PUT /api/admin/profiles/{name}'s answer.
type PutProfileResult struct {
	Profile   *NodeProfile `json:"profile,omitempty"`
	Applied   []string     `json:"applied"`
	Conflicts []string     `json:"conflicts"`
}

// DeleteProfileResult is DELETE /api/admin/profiles/{name}'s answer.
type DeleteProfileResult struct {
	Reasserted []string `json:"reasserted"`
	Extra      JSONDict `json:"-"`
}

// NodeProfileState is GET /api/admin/nodes/{id}/profile's answer — desired vs. effective, and the
// refusals in between.
type NodeProfileState struct {
	NodeID    string     `json:"nodeId"`
	Desired   JSONDict   `json:"desired,omitempty"`
	Effective JSONDict   `json:"effective,omitempty"`
	Refusals  []JSONDict `json:"refusals"`
	Extra     JSONDict   `json:"-"`
}

var nodeProfileStateKnown = []string{"nodeId", "desired", "effective", "refusals"}

// ModelCommandAccepted is the literal 202 body a pull/delete/warm command answers with. Reused
// means somebody already asked — surfaced, not hidden: a caller polling for their own command id
// needs to know it may be watching someone else's.
type ModelCommandAccepted struct {
	NodeID    string   `json:"nodeId"`
	Model     string   `json:"model"`
	Kind      string   `json:"kind"`
	CommandID string   `json:"commandId"`
	Reused    bool     `json:"reused"`
	Extra     JSONDict `json:"-"`
}

var modelCommandAcceptedKnown = []string{"nodeId", "model", "kind", "commandId", "reused"}

// FleetModelMatrix is GET /api/admin/models — kept as a thin wrapper over the wire shape rather
// than a typed grid: which nodes hold each model is exactly the shape the hub sends.
type FleetModelMatrix struct {
	Models []JSONDict `json:"models"`
	Nodes  []JSONDict `json:"nodes"`
	Extra  JSONDict   `json:"-"`
}

var fleetModelMatrixKnown = []string{"models", "nodes"}

// EnsureModelResult is POST /api/admin/models/{model}/ensure's answer — the hub's full placement
// reasoning, not just a boolean (dotnet D3).
type EnsureModelResult struct {
	Satisfied bool     `json:"satisfied"`
	Decision  JSONDict `json:"decision"`
	Extra     JSONDict `json:"-"`
}

var ensureModelResultKnown = []string{"satisfied", "decision"}

// UsageRow is GET /api/admin/usage's per-(client, model) projection — counts only, never a prompt
// or a completion (hub rule 7).
type UsageRow struct {
	ClientID         string `json:"clientId"`
	Model            string `json:"model"`
	Requests         int64  `json:"requests"`
	PromptTokens     int64  `json:"promptTokens"`
	CompletionTokens int64  `json:"completionTokens"`
	TotalTokens      int64  `json:"totalTokens"`
	FallbackRequests int64  `json:"fallbackRequests"`
}

// UsageResponse is GET /api/admin/usage's answer.
type UsageResponse struct {
	Rows []UsageRow `json:"rows"`
}

// ClientRow is GET /api/admin/clients's answer — never carries a key (dotnet D5: ClientConfig.Key
// never leaves the hub process).
type ClientRow struct {
	ClientID  string   `json:"clientId"`
	Limits    JSONDict `json:"limits,omitempty"`
	LiveUsage JSONDict `json:"liveUsage,omitempty"`
	Extra     JSONDict `json:"-"`
}

var clientRowKnown = []string{"clientId", "limits", "liveUsage"}

// -- The node -----------------------------------------------------------------------------------

// NodeBackendInfo is a solo node's own backend (its Ollama, or an upstream cloud provider).
type NodeBackendInfo struct {
	Name     string `json:"name,omitempty"`
	Endpoint string `json:"endpoint,omitempty"`
	Health   string `json:"health,omitempty"`
}

// NodeConcurrency is a solo node's request concurrency.
type NodeConcurrency struct {
	Limit    int `json:"limit"`
	InFlight int `json:"inFlight"`
}

// NodeGPUInfo is a solo node's GPU inventory.
type NodeGPUInfo struct {
	CUDA    bool     `json:"cuda"`
	Devices int      `json:"devices"`
	Names   []string `json:"names"`
}

// NodeRetrievalInfo is a solo node's retrieval config. Rerank is a **string** (the config-level
// rerank mode, "none"/"llm"), never a boolean — the conformance corpus's founding case
// (node-status-rerank-is-a-string): dotnet typed it bool? in v1.7.0 and threw the first time it
// was driven against a real node with retrieval on. This client is typed correctly from the start
// because the case exists before the bug had a chance to happen here.
type NodeRetrievalInfo struct {
	Enabled        bool       `json:"enabled"`
	Provider       string     `json:"provider,omitempty"`
	EmbeddingModel string     `json:"embeddingModel,omitempty"`
	Mode           string     `json:"mode,omitempty"`
	Rerank         string     `json:"rerank,omitempty"`
	Collections    []JSONDict `json:"collections"`
	Error          string     `json:"error,omitempty"`
}

// NodeStatusResponse is a solo node's GET /api/status — deliberately a smaller, different document
// than StatusResponse. Mode is always "solo" and is the only field that tells the two documents
// apart; there is no fleet array, no queue block, no replica count.
type NodeStatusResponse struct {
	Mode         string             `json:"mode"`
	NodeVersion  string             `json:"nodeVersion,omitempty"`
	NowUTC       string             `json:"nowUtc,omitempty"`
	Name         string             `json:"name,omitempty"`
	Backend      *NodeBackendInfo   `json:"backend,omitempty"`
	Concurrency  *NodeConcurrency   `json:"concurrency,omitempty"`
	GPU          *NodeGPUInfo       `json:"gpu,omitempty"`
	Capabilities []string           `json:"capabilities"`
	Retrieval    *NodeRetrievalInfo `json:"retrieval,omitempty"`
	Models       []ModelInfo        `json:"models"`
	Extra        JSONDict           `json:"-"`
}

var nodeStatusKnown = []string{
	"mode", "nodeVersion", "nowUtc", "name", "backend", "concurrency", "gpu", "capabilities",
	"retrieval", "models",
}

// TargetKind is "hub" or "solo_node" — Probe's discriminator.
type TargetKind string

const (
	TargetHub      TargetKind = "hub"
	TargetSoloNode TargetKind = "solo_node"
)

// TargetProbe is Probe's answer — one GET /api/status, discriminated on whether the body carries
// mode (present → a solo node; the hub's document never has the field at all). Exactly one of
// HubStatus/NodeStatus is set, matching Kind.
type TargetProbe struct {
	Kind       TargetKind
	Version    string
	HubStatus  *StatusResponse
	NodeStatus *NodeStatusResponse
}

func targetProbeFromJSON(raw JSONDict, body []byte) (TargetProbe, error) {
	if _, hasMode := raw["mode"]; hasMode {
		var status NodeStatusResponse
		if err := json.Unmarshal(body, &status); err != nil {
			return TargetProbe{}, fmt.Errorf("inferhub: decoding node status: %w", err)
		}
		status.Extra = extractExtra(body, nodeStatusKnown)
		return TargetProbe{Kind: TargetSoloNode, Version: status.NodeVersion, NodeStatus: &status}, nil
	}
	var status StatusResponse
	if err := json.Unmarshal(body, &status); err != nil {
		return TargetProbe{}, fmt.Errorf("inferhub: decoding hub status: %w", err)
	}
	version := ""
	if status.CoordinatorVersion != nil {
		version = *status.CoordinatorVersion
	}
	return TargetProbe{Kind: TargetHub, Version: version, HubStatus: &status}, nil
}

// -- HTTP helpers ---------------------------------------------------------------------------------

func (c *Client) adminGet(ctx context.Context, path string) (*http.Response, error) {
	return c.adminDo(ctx, http.MethodGet, path, nil)
}

func (c *Client) adminDo(ctx context.Context, method, path string, body JSONDict) (*http.Response, error) {
	var encoded []byte
	if body != nil {
		var err error
		encoded, err = json.Marshal(body)
		if err != nil {
			return nil, fmt.Errorf("inferhub: encoding %s %s request: %w", method, path, err)
		}
	}
	req, err := c.newRequest(ctx, method, path, encoded)
	if err != nil {
		return nil, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	return resp, nil
}

func readBody(resp *http.Response) ([]byte, error) {
	defer resp.Body.Close()
	return io.ReadAll(resp.Body)
}

// -- Fleet ops --------------------------------------------------------------------------------------

// ListNodes is GET /api/admin/nodes.
func (c *Client) ListNodes(ctx context.Context) ([]AdminNode, error) {
	resp, err := c.adminGet(ctx, "api/admin/nodes")
	if err != nil {
		return nil, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return nil, err
	}
	var raw []JSONDict
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		resp.Body.Close()
		return nil, fmt.Errorf("inferhub: decoding node list: %w", err)
	}
	resp.Body.Close()
	nodes := make([]AdminNode, 0, len(raw))
	for _, n := range raw {
		nodes = append(nodes, adminNodeFromJSON(n))
	}
	return nodes, nil
}

func (c *Client) adminAction(ctx context.Context, path string) error {
	resp, err := c.adminDo(ctx, http.MethodPost, path, nil)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	return raiseForStatus(resp)
}

// Cordon is POST /api/admin/nodes/{id}/cordon.
func (c *Client) Cordon(ctx context.Context, nodeID string) error {
	return c.adminAction(ctx, "api/admin/nodes/"+nodeID+"/cordon")
}

// Uncordon is POST /api/admin/nodes/{id}/uncordon.
func (c *Client) Uncordon(ctx context.Context, nodeID string) error {
	return c.adminAction(ctx, "api/admin/nodes/"+nodeID+"/uncordon")
}

// Deregister is POST /api/admin/nodes/{id}/deregister.
func (c *Client) Deregister(ctx context.Context, nodeID string) error {
	return c.adminAction(ctx, "api/admin/nodes/"+nodeID+"/deregister")
}

// -- Vector collections (admin plane) ----------------------------------------------------------------

// ListAdminCollections is GET /api/admin/vector/collections — with replica placement. Not
// ListNodeCollections: different auth, different route, different shape.
func (c *Client) ListAdminCollections(ctx context.Context) (CollectionsResponse, error) {
	var out CollectionsResponse
	resp, err := c.adminGet(ctx, "api/admin/vector/collections")
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading admin collections response: %w", err)
	}
	var raw struct {
		Collections []JSONDict `json:"collections"`
	}
	if err := json.Unmarshal(body, &raw); err != nil {
		return out, fmt.Errorf("inferhub: decoding admin collections response: %w", err)
	}
	collections := make([]CollectionInfo, 0, len(raw.Collections))
	for _, item := range raw.Collections {
		encoded, _ := json.Marshal(item)
		var ci CollectionInfo
		if err := ci.populateFrom(encoded); err != nil {
			return out, fmt.Errorf("inferhub: decoding collection info: %w", err)
		}
		collections = append(collections, ci)
	}
	return CollectionsResponse{Collections: collections, Extra: extractExtra(body, []string{"collections"})}, nil
}

// GetAdminCollection is GET /api/admin/vector/collections/{name}. Returns
// (CollectionDetail{}, false, nil) on 404.
func (c *Client) GetAdminCollection(ctx context.Context, collection string) (CollectionDetail, bool, error) {
	var out CollectionDetail
	resp, err := c.adminGet(ctx, "api/admin/vector/collections/"+collection)
	if err != nil {
		return out, false, err
	}
	if resp.StatusCode == http.StatusNotFound {
		resp.Body.Close()
		return out, false, nil
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, false, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, false, fmt.Errorf("inferhub: reading collection detail: %w", err)
	}
	if err := json.Unmarshal(body, &out); err != nil {
		return out, false, fmt.Errorf("inferhub: decoding collection detail: %w", err)
	}
	out.Extra = extractExtra(body, collectionDetailKnown)
	return out, true, nil
}

// CreateAdminCollection is POST /api/admin/vector/collections.
func (c *Client) CreateAdminCollection(ctx context.Context, name string, dimension int, distance string) (CollectionInfo, error) {
	var out CollectionInfo
	body := JSONDict{"name": name, "dimension": dimension}
	if distance != "" {
		body["distance"] = distance
	}
	resp, err := c.adminDo(ctx, http.MethodPost, "api/admin/vector/collections", body)
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	encoded, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading collection response: %w", err)
	}
	if err := out.populateFrom(encoded); err != nil {
		return out, fmt.Errorf("inferhub: decoding collection response: %w", err)
	}
	return out, nil
}

// DropAdminCollection is DELETE /api/admin/vector/collections/{name}.
func (c *Client) DropAdminCollection(ctx context.Context, collection string) error {
	resp, err := c.adminDo(ctx, http.MethodDelete, "api/admin/vector/collections/"+collection, nil)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	return raiseForStatus(resp)
}

// RebuildAdminCollection is POST /api/admin/vector/collections/{name}/rebuild.
func (c *Client) RebuildAdminCollection(ctx context.Context, collection string) error {
	return c.adminAction(ctx, "api/admin/vector/collections/"+collection+"/rebuild")
}

// AdminEvent is one frame of GET /api/admin/stream — Event is the SSE event name (snapshot,
// vector.*, model-progress, ...), Data its parsed JSON payload.
type AdminEvent struct {
	Event string
	Data  JSONDict
}

// AdminEventStream iterates StreamAdminEvents. Ends when the server closes the stream; no
// reconnect variant — a caller that wants one wraps this in their own retry loop.
type AdminEventStream struct {
	reader *sseFrameReader
	cur    AdminEvent
}

func (s *AdminEventStream) Next() bool {
	if !s.reader.Next() {
		return false
	}
	frame := s.reader.Value()
	s.cur = AdminEvent{Event: frame.Event, Data: frame.Data}
	return true
}
func (s *AdminEventStream) Value() AdminEvent { return s.cur }
func (s *AdminEventStream) Err() error        { return s.reader.Err() }
func (s *AdminEventStream) Close() error      { return s.reader.Close() }

// StreamAdminEvents is GET /api/admin/stream (SSE) — fleet snapshot events and vector.* lifecycle
// events.
func (c *Client) StreamAdminEvents(ctx context.Context) (*AdminEventStream, error) {
	resp, err := c.adminGet(ctx, "api/admin/stream")
	if err != nil {
		return nil, err
	}
	if err := raiseForStatus(resp); err != nil {
		return nil, err
	}
	return &AdminEventStream{reader: newSSEFrameReader(resp.Body)}, nil
}

// -- Node profiles ------------------------------------------------------------------------------------

func nodeProfileFromBody(body []byte) (NodeProfile, error) {
	var p NodeProfile
	if err := json.Unmarshal(body, &p); err != nil {
		return p, err
	}
	p.Extra = extractExtra(body, nodeProfileKnown)
	return p, nil
}

// ListProfiles is GET /api/admin/profiles.
func (c *Client) ListProfiles(ctx context.Context) ([]NodeProfile, error) {
	resp, err := c.adminGet(ctx, "api/admin/profiles")
	if err != nil {
		return nil, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return nil, err
	}
	body, err := readBody(resp)
	if err != nil {
		return nil, fmt.Errorf("inferhub: reading profile list: %w", err)
	}
	var raw []json.RawMessage
	if err := json.Unmarshal(body, &raw); err != nil {
		return nil, fmt.Errorf("inferhub: decoding profile list: %w", err)
	}
	profiles := make([]NodeProfile, 0, len(raw))
	for _, item := range raw {
		p, err := nodeProfileFromBody(item)
		if err != nil {
			return nil, fmt.Errorf("inferhub: decoding profile: %w", err)
		}
		profiles = append(profiles, p)
	}
	return profiles, nil
}

// GetProfile is GET /api/admin/profiles/{name}. Returns (NodeProfile{}, false, nil) on 404.
func (c *Client) GetProfile(ctx context.Context, name string) (NodeProfile, bool, error) {
	resp, err := c.adminGet(ctx, "api/admin/profiles/"+name)
	if err != nil {
		return NodeProfile{}, false, err
	}
	if resp.StatusCode == http.StatusNotFound {
		resp.Body.Close()
		return NodeProfile{}, false, nil
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return NodeProfile{}, false, err
	}
	body, err := readBody(resp)
	if err != nil {
		return NodeProfile{}, false, fmt.Errorf("inferhub: reading profile: %w", err)
	}
	p, err := nodeProfileFromBody(body)
	return p, true, err
}

// PutProfile is PUT /api/admin/profiles/{name} — creates or replaces. profile.Name/.Revision are
// ignored; the hub sets both from the route and its own counter.
func (c *Client) PutProfile(ctx context.Context, name string, profile NodeProfile) (PutProfileResult, error) {
	var out PutProfileResult
	resp, err := c.adminDo(ctx, http.MethodPut, "api/admin/profiles/"+name, profile.toJSON())
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading put-profile response: %w", err)
	}
	var raw struct {
		Profile   *json.RawMessage `json:"profile"`
		Applied   []string         `json:"applied"`
		Conflicts []string         `json:"conflicts"`
	}
	if err := json.Unmarshal(body, &raw); err != nil {
		return out, fmt.Errorf("inferhub: decoding put-profile response: %w", err)
	}
	out.Applied, out.Conflicts = raw.Applied, raw.Conflicts
	if raw.Profile != nil {
		p, err := nodeProfileFromBody(*raw.Profile)
		if err != nil {
			return out, fmt.Errorf("inferhub: decoding put-profile's profile: %w", err)
		}
		out.Profile = &p
	}
	return out, nil
}

// DeleteProfile is DELETE /api/admin/profiles/{name}.
func (c *Client) DeleteProfile(ctx context.Context, name string) (DeleteProfileResult, error) {
	var out DeleteProfileResult
	resp, err := c.adminDo(ctx, http.MethodDelete, "api/admin/profiles/"+name, nil)
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading delete-profile response: %w", err)
	}
	if err := json.Unmarshal(body, &out); err != nil {
		return out, fmt.Errorf("inferhub: decoding delete-profile response: %w", err)
	}
	out.Extra = extractExtra(body, []string{"reasserted"})
	return out, nil
}

// GetNodeProfile is GET /api/admin/nodes/{id}/profile — desired vs. effective, and the refusals in
// between.
func (c *Client) GetNodeProfile(ctx context.Context, nodeID string) (NodeProfileState, error) {
	var out NodeProfileState
	resp, err := c.adminGet(ctx, "api/admin/nodes/"+nodeID+"/profile")
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading node profile state: %w", err)
	}
	if err := json.Unmarshal(body, &out); err != nil {
		return out, fmt.Errorf("inferhub: decoding node profile state: %w", err)
	}
	out.Extra = extractExtra(body, nodeProfileStateKnown)
	return out, nil
}

// -- Model lifecycle ------------------------------------------------------------------------------

func (c *Client) modelCommand(ctx context.Context, method, path string) (ModelCommandAccepted, error) {
	var out ModelCommandAccepted
	resp, err := c.adminDo(ctx, method, path, nil)
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading model command response: %w", err)
	}
	if err := json.Unmarshal(body, &out); err != nil {
		return out, fmt.Errorf("inferhub: decoding model command response: %w", err)
	}
	out.Extra = extractExtra(body, modelCommandAcceptedKnown)
	return out, nil
}

// PullModel is POST /api/admin/nodes/{id}/models/{model}/pull.
func (c *Client) PullModel(ctx context.Context, nodeID, model string) (ModelCommandAccepted, error) {
	return c.modelCommand(ctx, http.MethodPost, "api/admin/nodes/"+nodeID+"/models/"+model+"/pull")
}

// DeleteModel is DELETE /api/admin/nodes/{id}/models/{model}.
func (c *Client) DeleteModel(ctx context.Context, nodeID, model string) (ModelCommandAccepted, error) {
	return c.modelCommand(ctx, http.MethodDelete, "api/admin/nodes/"+nodeID+"/models/"+model)
}

// WarmModel is POST /api/admin/nodes/{id}/models/{model}/warm.
func (c *Client) WarmModel(ctx context.Context, nodeID, model string) (ModelCommandAccepted, error) {
	return c.modelCommand(ctx, http.MethodPost, "api/admin/nodes/"+nodeID+"/models/"+model+"/warm")
}

// PullToolModel is POST /api/admin/nodes/{id}/tools/{tool}/models/{model}/pull.
func (c *Client) PullToolModel(ctx context.Context, nodeID, tool, model string) (ModelCommandAccepted, error) {
	return c.modelCommand(ctx, http.MethodPost, "api/admin/nodes/"+nodeID+"/tools/"+tool+"/models/"+model+"/pull")
}

// DeleteToolModel is DELETE /api/admin/nodes/{id}/tools/{tool}/models/{model}.
func (c *Client) DeleteToolModel(ctx context.Context, nodeID, tool, model string) (ModelCommandAccepted, error) {
	return c.modelCommand(ctx, http.MethodDelete, "api/admin/nodes/"+nodeID+"/tools/"+tool+"/models/"+model)
}

// ListModelMatrix is GET /api/admin/models — the fleet-wide model x node matrix.
func (c *Client) ListModelMatrix(ctx context.Context) (FleetModelMatrix, error) {
	var out FleetModelMatrix
	resp, err := c.adminGet(ctx, "api/admin/models")
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading model matrix: %w", err)
	}
	if err := json.Unmarshal(body, &out); err != nil {
		return out, fmt.Errorf("inferhub: decoding model matrix: %w", err)
	}
	out.Extra = extractExtra(body, fleetModelMatrixKnown)
	return out, nil
}

// EnsureModel is POST /api/admin/models/{model}/ensure — pulls onto the most suitable
// capable-and-manageable nodes that do not already have it, skipping cordoned ones. replicas <= 0
// means "let the hub decide".
func (c *Client) EnsureModel(ctx context.Context, model string, replicas int) (EnsureModelResult, error) {
	var out EnsureModelResult
	path := "api/admin/models/" + model + "/ensure"
	if replicas > 0 {
		path += fmt.Sprintf("?replicas=%d", replicas)
	}
	resp, err := c.adminDo(ctx, http.MethodPost, path, nil)
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading ensure-model response: %w", err)
	}
	if err := json.Unmarshal(body, &out); err != nil {
		return out, fmt.Errorf("inferhub: decoding ensure-model response: %w", err)
	}
	out.Extra = extractExtra(body, ensureModelResultKnown)
	return out, nil
}

// -- Usage and clients ----------------------------------------------------------------------------

// UsageQuery filters QueryUsage. A zero value returns everything.
type UsageQuery struct {
	From     time.Time
	To       time.Time
	ClientID string
	Model    string
}

func (q UsageQuery) queryString() string {
	params := url.Values{}
	if !q.From.IsZero() {
		params.Set("from", q.From.UTC().Format(time.RFC3339))
	}
	if !q.To.IsZero() {
		params.Set("to", q.To.UTC().Format(time.RFC3339))
	}
	if q.ClientID != "" {
		params.Set("clientId", q.ClientID)
	}
	if q.Model != "" {
		params.Set("model", q.Model)
	}
	if len(params) == 0 {
		return ""
	}
	return "?" + params.Encode()
}

// QueryUsage is GET /api/admin/usage — aggregates only, never a prompt or a completion.
func (c *Client) QueryUsage(ctx context.Context, query UsageQuery) (UsageResponse, error) {
	var out UsageResponse
	resp, err := c.adminGet(ctx, "api/admin/usage"+query.queryString())
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading usage response: %w", err)
	}
	// The hub answers either {"rows":[...]} or a bare [...] array.
	var wrapped struct {
		Rows []UsageRow `json:"rows"`
	}
	if err := json.Unmarshal(body, &wrapped); err == nil && wrapped.Rows != nil {
		return UsageResponse{Rows: wrapped.Rows}, nil
	}
	var bare []UsageRow
	if err := json.Unmarshal(body, &bare); err != nil {
		return out, fmt.Errorf("inferhub: decoding usage response: %w", err)
	}
	return UsageResponse{Rows: bare}, nil
}

// ListClients is GET /api/admin/clients — never carries a key.
func (c *Client) ListClients(ctx context.Context) ([]ClientRow, error) {
	resp, err := c.adminGet(ctx, "api/admin/clients")
	if err != nil {
		return nil, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return nil, err
	}
	body, err := readBody(resp)
	if err != nil {
		return nil, fmt.Errorf("inferhub: reading client list: %w", err)
	}
	var raw []json.RawMessage
	if err := json.Unmarshal(body, &raw); err != nil {
		return nil, fmt.Errorf("inferhub: decoding client list: %w", err)
	}
	rows := make([]ClientRow, 0, len(raw))
	for _, item := range raw {
		var row ClientRow
		if err := json.Unmarshal(item, &row); err != nil {
			return nil, fmt.Errorf("inferhub: decoding client row: %w", err)
		}
		row.Extra = extractExtra(item, clientRowKnown)
		rows = append(rows, row)
	}
	return rows, nil
}

// -- The node (root rule 6 / roadmap D7): a base address, not a second client -----------------

// Probe is GET /api/status — one round trip, discriminated on whether the body carries mode (a
// solo node) or not (the hub — its document never has the field).
func (c *Client) Probe(ctx context.Context) (TargetProbe, error) {
	resp, err := c.adminGet(ctx, "api/status")
	if err != nil {
		return TargetProbe{}, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return TargetProbe{}, err
	}
	body, err := readBody(resp)
	if err != nil {
		return TargetProbe{}, fmt.Errorf("inferhub: reading status response: %w", err)
	}
	var raw JSONDict
	if err := json.Unmarshal(body, &raw); err != nil {
		return TargetProbe{}, fmt.Errorf("inferhub: decoding status response: %w", err)
	}
	return targetProbeFromJSON(raw, body)
}

// GetNodeVersion is GET /api/version — node-only; a 404 against a hub means "wrong target," not
// "wrong version."
func (c *Client) GetNodeVersion(ctx context.Context) (string, error) {
	resp, err := c.adminGet(ctx, "api/version")
	if err != nil {
		return "", err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return "", err
	}
	var out struct {
		Version string `json:"version"`
	}
	defer resp.Body.Close()
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return "", fmt.Errorf("inferhub: decoding node version response: %w", err)
	}
	return out.Version, nil
}

// ListNodeCollections is GET /api/collections — node-only vector collection lifecycle; not the
// admin-gated /api/admin/vector/collections (different auth, different shape, no
// placement/replica info — a node has no fleet to place a replica on).
func (c *Client) ListNodeCollections(ctx context.Context) ([]CollectionInfo, error) {
	resp, err := c.adminGet(ctx, "api/collections")
	if err != nil {
		return nil, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return nil, err
	}
	body, err := readBody(resp)
	if err != nil {
		return nil, fmt.Errorf("inferhub: reading node collections: %w", err)
	}
	var raw struct {
		Collections []json.RawMessage `json:"collections"`
	}
	if err := json.Unmarshal(body, &raw); err != nil {
		return nil, fmt.Errorf("inferhub: decoding node collections: %w", err)
	}
	collections := make([]CollectionInfo, 0, len(raw.Collections))
	for _, item := range raw.Collections {
		var ci CollectionInfo
		if err := ci.populateFrom(item); err != nil {
			return nil, fmt.Errorf("inferhub: decoding node collection: %w", err)
		}
		collections = append(collections, ci)
	}
	return collections, nil
}

// GetNodeCollection is GET /api/collections/{name}. Returns (CollectionInfo{}, false, nil) on 404.
func (c *Client) GetNodeCollection(ctx context.Context, name string) (CollectionInfo, bool, error) {
	var out CollectionInfo
	resp, err := c.adminGet(ctx, "api/collections/"+name)
	if err != nil {
		return out, false, err
	}
	if resp.StatusCode == http.StatusNotFound {
		resp.Body.Close()
		return out, false, nil
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, false, err
	}
	body, err := readBody(resp)
	if err != nil {
		return out, false, fmt.Errorf("inferhub: reading node collection: %w", err)
	}
	if err := out.populateFrom(body); err != nil {
		return out, false, fmt.Errorf("inferhub: decoding node collection: %w", err)
	}
	return out, true, nil
}

// CreateNodeCollection is POST /api/collections.
func (c *Client) CreateNodeCollection(ctx context.Context, name string, dimension int, distance string) (CollectionInfo, error) {
	var out CollectionInfo
	body := JSONDict{"name": name, "dimension": dimension}
	if distance != "" {
		body["distance"] = distance
	}
	resp, err := c.adminDo(ctx, http.MethodPost, "api/collections", body)
	if err != nil {
		return out, err
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return out, err
	}
	encoded, err := readBody(resp)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading node collection response: %w", err)
	}
	if err := out.populateFrom(encoded); err != nil {
		return out, fmt.Errorf("inferhub: decoding node collection response: %w", err)
	}
	return out, nil
}

// DropNodeCollection is DELETE /api/collections/{name}.
func (c *Client) DropNodeCollection(ctx context.Context, name string) error {
	resp, err := c.adminDo(ctx, http.MethodDelete, "api/collections/"+name, nil)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	return raiseForStatus(resp)
}

// The hub's 501 not_supported refusals on GET /v1/videos and POST /v1/videos/{id}/remix (root
// rule 10) — taught here rather than published as methods that can only return an error. There is
// no video module in this client: an id is itself the capability to fetch the bytes, and nothing
// durable holds the prompt that made a clip, so neither a listing nor a remix can ever be served.
// See go/README.md's Video section for the recorded refusal bodies and the alternative (send a new
// request with the prompt you want).
const VideoErrorCodeNotSupported = "not_supported"
