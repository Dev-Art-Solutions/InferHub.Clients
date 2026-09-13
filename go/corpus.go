package inferhub

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"mime/multipart"
	"net/http"
)

// Ingestion and search — /api/collections/{collection}/** — go/v0.2.0 (D2/D3 in
// plans/phase-23-go-retrieval.md). Kept in their own file, same argument js 20 D3 gives
// _corpus.ts: the corpus surface is seven methods with multipart upload and its own
// 500-with-a-body shape, and folding it into client.go would leave that file covering three
// unrelated planes.

// TextDocument is the body of POST /api/collections/{collection}/documents when the document is
// supplied as text (JSON body, not multipart).
type TextDocument struct {
	ID       string
	Text     string
	Metadata JSONDict
}

func (d TextDocument) toJSON() JSONDict {
	body := JSONDict{"id": d.ID, "text": d.Text}
	if d.Metadata != nil {
		body["metadata"] = d.Metadata
	}
	return body
}

// FileDocument is a document supplied as a file, multipart. Body is read once and never copied
// into memory beyond what encoding/multipart itself buffers — this client never opens a file
// itself and never holds content past the request (root rule 4, extended to corpus content).
type FileDocument struct {
	ID          string
	Filename    string
	Body        io.Reader
	ContentType string
	Metadata    JSONDict
}

// IngestResult is the hub's own answer to an ingest call — "ingested", "unchanged" or "partial".
// A "partial" result arrives as an HTTP 500 **with this exact body**, and IngestText/IngestFile
// return it rather than erroring (root rule 11).
type IngestResult struct {
	DocumentID     string   `json:"documentId"`
	Collection     string   `json:"collection"`
	Status         string   `json:"status"`
	Chunks         int      `json:"chunks"`
	ChunksEmbedded int      `json:"chunksEmbedded"`
	Bytes          int64    `json:"bytes"`
	ContentHash    string   `json:"contentHash,omitempty"`
	Error          string   `json:"error,omitempty"`
	Extra          JSONDict `json:"-"`
}

var ingestResultKnownFields = []string{
	"documentId", "collection", "status", "chunks", "chunksEmbedded", "bytes", "contentHash", "error",
}

func (r *IngestResult) UnmarshalJSON(data []byte) error {
	var raw JSONDict
	if err := json.Unmarshal(data, &raw); err != nil {
		return err
	}
	type alias struct {
		DocumentID     string `json:"documentId"`
		Collection     string `json:"collection"`
		Status         string `json:"status"`
		Chunks         int    `json:"chunks"`
		ChunksEmbedded int    `json:"chunksEmbedded"`
		Bytes          int64  `json:"bytes"`
		ContentHash    string `json:"contentHash"`
		Error          string `json:"error"`
	}
	var a alias
	if err := json.Unmarshal(data, &a); err != nil {
		return err
	}
	r.DocumentID = a.DocumentID
	r.Collection = a.Collection
	r.Status = a.Status
	r.Chunks = a.Chunks
	r.ChunksEmbedded = a.ChunksEmbedded
	r.Bytes = a.Bytes
	r.ContentHash = a.ContentHash
	r.Error = a.Error
	for _, key := range ingestResultKnownFields {
		delete(raw, key)
	}
	r.Extra = raw
	return nil
}

// looksLikeIngestResult reports whether a body has both "documentId" and "status" — used to tell
// an IngestResult (even on a 500) apart from a genuine error envelope ({"error": ...} with
// neither field), matching js 20 D3's looksLikeIngestResult exactly.
func looksLikeIngestResult(raw JSONDict) bool {
	_, hasID := raw["documentId"]
	_, hasStatus := raw["status"]
	return hasID && hasStatus
}

// DocumentSummary is one entry of ListDocuments, and GetDocument's answer.
type DocumentSummary struct {
	DocumentID string   `json:"documentId"`
	Collection string   `json:"collection"`
	Status     string   `json:"status"`
	Chunks     int      `json:"chunks"`
	Bytes      int64    `json:"bytes"`
	Extra      JSONDict `json:"-"`
}

var documentSummaryKnownFields = []string{"documentId", "collection", "status", "chunks", "bytes"}

func (d *DocumentSummary) UnmarshalJSON(data []byte) error {
	var raw JSONDict
	if err := json.Unmarshal(data, &raw); err != nil {
		return err
	}
	type alias struct {
		DocumentID string `json:"documentId"`
		Collection string `json:"collection"`
		Status     string `json:"status"`
		Chunks     int    `json:"chunks"`
		Bytes      int64  `json:"bytes"`
	}
	var a alias
	if err := json.Unmarshal(data, &a); err != nil {
		return err
	}
	d.DocumentID = a.DocumentID
	d.Collection = a.Collection
	d.Status = a.Status
	d.Chunks = a.Chunks
	d.Bytes = a.Bytes
	for _, key := range documentSummaryKnownFields {
		delete(raw, key)
	}
	d.Extra = raw
	return nil
}

// DocumentChunk's Index is a string, not an int — the hub's chunk metadata is a string map; Page,
// when present, is a real number on the same response (the asymmetry conformance case
// "chunk-index-is-a-string-not-an-int" exists to catch).
type DocumentChunk struct {
	ID    string `json:"id"`
	Index string `json:"index"`
	Page  *int   `json:"page,omitempty"`
	Text  string `json:"text"`
}

// DocumentChunksResponse is the body of GET .../documents/{documentId}/chunks.
type DocumentChunksResponse struct {
	Collection string          `json:"collection"`
	DocumentID string          `json:"documentId"`
	Chunks     []DocumentChunk `json:"chunks"`
}

// DocumentDeletion is the body of DELETE .../documents/{documentId}.
type DocumentDeletion struct {
	DocumentID string `json:"documentId"`
	Deleted    bool   `json:"deleted"`
}

// SearchRequest is the body of POST /api/collections/{collection}/search. Mode/Rerank are body
// fields here — unlike chat/generate, search takes them in the request rather than as headers.
type SearchRequest struct {
	Query  string
	TopK   int
	Mode   string
	Rerank *bool
	Filter JSONDict
}

func (r SearchRequest) toJSON() JSONDict {
	topK := r.TopK
	if topK == 0 {
		topK = 10
	}
	body := JSONDict{"query": r.Query, "topK": topK}
	if r.Mode != "" {
		body["mode"] = r.Mode
	}
	if r.Rerank != nil {
		body["rerank"] = *r.Rerank
	}
	if r.Filter != nil {
		body["filter"] = r.Filter
	}
	return body
}

// SearchHit is one hit of Search.
type SearchHit struct {
	ID         string  `json:"id"`
	Score      float64 `json:"score"`
	DocumentID string  `json:"documentId"`
	Text       string  `json:"text"`
}

// SearchResponse is the body of Search. Hits stays in the hub's own wire order, never re-sorted by
// score (root rule 11 — a reranked result routinely has a lower score above a higher one).
type SearchResponse struct {
	Collection string      `json:"collection"`
	Mode       string      `json:"mode"`
	Hits       []SearchHit `json:"hits"`
}

func (c *Client) corpusJSONRequest(ctx context.Context, method, path string, body JSONDict) (*http.Response, error) {
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

// ingestResultOrError decodes an ingest response, returning the IngestResult even on a non-2xx
// status when the body looks like one (D2's partial-vs-error split, mirroring js 20's
// ingestResultOrThrow) — otherwise it falls back to raiseForStatus.
func ingestResultOrError(resp *http.Response) (IngestResult, error) {
	var out IngestResult
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		bodyBytes, _ := io.ReadAll(resp.Body)
		resp.Body.Close()
		var raw JSONDict
		if err := json.Unmarshal(bodyBytes, &raw); err == nil && looksLikeIngestResult(raw) {
			if err := json.Unmarshal(bodyBytes, &out); err != nil {
				return out, fmt.Errorf("inferhub: decoding partial ingest response: %w", err)
			}
			return out, nil
		}
		return out, raiseForStatusBody(resp.StatusCode, resp.Header, bodyBytes)
	}
	defer resp.Body.Close()
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding ingest response: %w", err)
	}
	return out, nil
}

// IngestText is POST /api/collections/{collection}/documents with a JSON body.
func (c *Client) IngestText(ctx context.Context, collection string, document TextDocument) (IngestResult, error) {
	resp, err := c.corpusJSONRequest(ctx, http.MethodPost, "api/collections/"+collection+"/documents", document.toJSON())
	if err != nil {
		return IngestResult{}, err
	}
	return ingestResultOrError(resp)
}

// IngestFile is POST /api/collections/{collection}/documents as multipart, via mime/multipart —
// the platform's own encoder, no dependency (D3). The file field is appended last: some multipart
// parsers are order-sensitive, matching python 17 D4 / js 20 D4's ordering argument.
func (c *Client) IngestFile(ctx context.Context, collection string, document FileDocument) (IngestResult, error) {
	var buf bytes.Buffer
	writer := multipart.NewWriter(&buf)
	if err := writer.WriteField("id", document.ID); err != nil {
		return IngestResult{}, fmt.Errorf("inferhub: writing id field: %w", err)
	}
	if document.Metadata != nil {
		encoded, err := json.Marshal(document.Metadata)
		if err != nil {
			return IngestResult{}, fmt.Errorf("inferhub: encoding metadata field: %w", err)
		}
		if err := writer.WriteField("metadata", string(encoded)); err != nil {
			return IngestResult{}, fmt.Errorf("inferhub: writing metadata field: %w", err)
		}
	}
	part, err := writer.CreatePart(fileHeader(document.Filename, document.ContentType))
	if err != nil {
		return IngestResult{}, fmt.Errorf("inferhub: creating file part: %w", err)
	}
	if _, err := io.Copy(part, document.Body); err != nil {
		return IngestResult{}, fmt.Errorf("inferhub: writing file content: %w", err)
	}
	if err := writer.Close(); err != nil {
		return IngestResult{}, fmt.Errorf("inferhub: closing multipart writer: %w", err)
	}

	target, err := c.resolve("api/collections/" + collection + "/documents")
	if err != nil {
		return IngestResult{}, fmt.Errorf("inferhub: %w", err)
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, target, &buf)
	if err != nil {
		return IngestResult{}, fmt.Errorf("inferhub: %w", err)
	}
	for k, v := range c.buildHeaders() {
		req.Header[k] = v
	}
	req.Header.Set("Content-Type", writer.FormDataContentType())
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return IngestResult{}, fmt.Errorf("inferhub: %w", err)
	}
	return ingestResultOrError(resp)
}

func fileHeader(filename, contentType string) map[string][]string {
	if contentType == "" {
		contentType = "application/octet-stream"
	}
	return map[string][]string{
		"Content-Disposition": {fmt.Sprintf(`form-data; name="file"; filename=%q`, filename)},
		"Content-Type":        {contentType},
	}
}

// ListDocuments is GET /api/collections/{collection}/documents.
func (c *Client) ListDocuments(ctx context.Context, collection string) ([]DocumentSummary, error) {
	resp, err := c.corpusJSONRequest(ctx, http.MethodGet, "api/collections/"+collection+"/documents", nil)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return nil, err
	}
	var out struct {
		Documents []DocumentSummary `json:"documents"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return nil, fmt.Errorf("inferhub: decoding document list response: %w", err)
	}
	return out.Documents, nil
}

// GetDocument is GET /api/collections/{collection}/documents/{documentId}. Returns
// (DocumentSummary{}, false, nil) on 404, never an error (root rule 12).
func (c *Client) GetDocument(ctx context.Context, collection, documentID string) (DocumentSummary, bool, error) {
	var out DocumentSummary
	resp, err := c.corpusJSONRequest(ctx, http.MethodGet, "api/collections/"+collection+"/documents/"+documentID, nil)
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
		return out, false, fmt.Errorf("inferhub: decoding document response: %w", err)
	}
	return out, true, nil
}

// GetChunks is GET /api/collections/{collection}/documents/{documentId}/chunks.
func (c *Client) GetChunks(ctx context.Context, collection, documentID string) (DocumentChunksResponse, error) {
	var out DocumentChunksResponse
	resp, err := c.corpusJSONRequest(ctx, http.MethodGet, "api/collections/"+collection+"/documents/"+documentID+"/chunks", nil)
	if err != nil {
		return out, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding document chunks response: %w", err)
	}
	return out, nil
}

// DeleteDocument is DELETE /api/collections/{collection}/documents/{documentId}. Returns
// (DocumentDeletion{}, false, nil) on 404.
func (c *Client) DeleteDocument(ctx context.Context, collection, documentID string) (DocumentDeletion, bool, error) {
	var out DocumentDeletion
	resp, err := c.corpusJSONRequest(ctx, http.MethodDelete, "api/collections/"+collection+"/documents/"+documentID, nil)
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
		return out, false, fmt.Errorf("inferhub: decoding document deletion response: %w", err)
	}
	return out, true, nil
}

// Search is POST /api/collections/{collection}/search. Unlike GetDocument/GetChunks/
// DeleteDocument, this **errors** on a collection that does not exist (root rule 12): answering
// "no hits" for a misspelled collection reports an empty corpus as a working one.
func (c *Client) Search(ctx context.Context, collection string, request SearchRequest) (SearchResponse, error) {
	var out SearchResponse
	resp, err := c.corpusJSONRequest(ctx, http.MethodPost, "api/collections/"+collection+"/search", request.toJSON())
	if err != nil {
		return out, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding search response: %w", err)
	}
	return out, nil
}
