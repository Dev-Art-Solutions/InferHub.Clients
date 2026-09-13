package inferhub

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
)

// The vector data-plane — POST/GET/DELETE /api/vector/{collection}/** — go/v0.2.0 (D2 in
// plans/phase-23-go-retrieval.md). Ports python _models.py's / js types.ts's vector shapes.

// VectorUpsert is the body of POST /api/vector/{collection}/upsert. Exactly one of Vector/Text is
// set — the hub embeds Text itself when a vector is not supplied.
type VectorUpsert struct {
	ID      string
	Vector  []float64
	Text    string
	Payload JSONDict
}

func (u VectorUpsert) toJSON() JSONDict {
	body := JSONDict{"id": u.ID}
	if u.Vector != nil {
		body["vector"] = u.Vector
	}
	if u.Text != "" {
		body["text"] = u.Text
	}
	if u.Payload != nil {
		body["payload"] = u.Payload
	}
	return body
}

// VectorQuery is the body of POST /api/vector/{collection}/query (or /retrieve — same shape).
// Exactly one of Vector/Text is set, same rule as VectorUpsert. TopK defaults to 10 when zero.
type VectorQuery struct {
	Vector []float64
	Text   string
	TopK   int
	Filter JSONDict
}

func (q VectorQuery) toJSON() JSONDict {
	topK := q.TopK
	if topK == 0 {
		topK = 10
	}
	body := JSONDict{"topK": topK}
	if q.Vector != nil {
		body["vector"] = q.Vector
	}
	if q.Text != "" {
		body["text"] = q.Text
	}
	if q.Filter != nil {
		body["filter"] = q.Filter
	}
	return body
}

// VectorMatch is one hit of Query/Retrieve.
type VectorMatch struct {
	ID      string   `json:"id"`
	Score   float64  `json:"score"`
	Payload JSONDict `json:"payload,omitempty"`
}

// VectorRecord is the body of Upsert's response and GetRecord's answer.
type VectorRecord struct {
	ID      string    `json:"id"`
	Vector  []float64 `json:"vector,omitempty"`
	Payload JSONDict  `json:"payload,omitempty"`
}

func (c *Client) doVectorRequest(ctx context.Context, method, path string, body JSONDict) (*http.Response, error) {
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

// Upsert is POST /api/vector/{collection}/upsert.
func (c *Client) Upsert(ctx context.Context, collection string, upsert VectorUpsert) (VectorRecord, error) {
	var out VectorRecord
	resp, err := c.doVectorRequest(ctx, http.MethodPost, "api/vector/"+collection+"/upsert", upsert.toJSON())
	if err != nil {
		return out, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding vector upsert response: %w", err)
	}
	return out, nil
}

func (c *Client) queryLike(ctx context.Context, path string, query VectorQuery) ([]VectorMatch, error) {
	resp, err := c.doVectorRequest(ctx, http.MethodPost, path, query.toJSON())
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return nil, err
	}
	var out struct {
		Matches []VectorMatch `json:"matches"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return nil, fmt.Errorf("inferhub: decoding vector query response: %w", err)
	}
	return out.Matches, nil
}

// Query is POST /api/vector/{collection}/query.
func (c *Client) Query(ctx context.Context, collection string, query VectorQuery) ([]VectorMatch, error) {
	return c.queryLike(ctx, "api/vector/"+collection+"/query", query)
}

// Retrieve is POST /api/vector/{collection}/retrieve — same shape as Query, the RAG-oriented route
// name the hub also answers on.
func (c *Client) Retrieve(ctx context.Context, collection string, query VectorQuery) ([]VectorMatch, error) {
	return c.queryLike(ctx, "api/vector/"+collection+"/retrieve", query)
}

// GetRecord is GET /api/vector/{collection}/{id}. Returns (VectorRecord{}, nil, false) on 404,
// never an error (root rule 12: a 404 naming one thing is an absence).
func (c *Client) GetRecord(ctx context.Context, collection, id string) (VectorRecord, bool, error) {
	var out VectorRecord
	resp, err := c.doVectorRequest(ctx, http.MethodGet, "api/vector/"+collection+"/"+id, nil)
	if err != nil {
		return out, false, err
	}
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusNotFound {
		return out, false, nil
	}
	if err := raiseForStatus(resp); err != nil {
		return out, false, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, false, fmt.Errorf("inferhub: decoding vector record response: %w", err)
	}
	return out, true, nil
}

// DeleteRecord is DELETE /api/vector/{collection}/{id}. Returns true iff a record was actually
// deleted; false (with a nil error) on 404.
func (c *Client) DeleteRecord(ctx context.Context, collection, id string) (bool, error) {
	resp, err := c.doVectorRequest(ctx, http.MethodDelete, "api/vector/"+collection+"/"+id, nil)
	if err != nil {
		return false, err
	}
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusNotFound {
		return false, nil
	}
	if err := raiseForStatus(resp); err != nil {
		return false, err
	}
	return true, nil
}
