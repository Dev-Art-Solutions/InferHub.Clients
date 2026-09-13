package inferhub

// Unit tests for corpus.go (go/v0.2.0) — ingestion/search request shaping, the partial-500 vs.
// genuine-error split (root rule 11), multipart field order, and the 404-is-not-an-error rule
// (root rule 12). Mirrors js/test/client.test.ts's corpus block and python 17's test_client.py.

import (
	"bytes"
	"context"
	"errors"
	"io"
	"mime"
	"mime/multipart"
	"net/http"
	"strings"
	"testing"
)

func TestIngestTextSendsJSONBody(t *testing.T) {
	var gotContentType, gotBody string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotContentType = r.Header.Get("Content-Type")
		b, _ := io.ReadAll(r.Body)
		gotBody = string(b)
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"documentId":"d1","collection":"docs","status":"ingested","chunks":3,"chunksEmbedded":3,"bytes":42}`))
	})
	defer closeSrv()

	result, err := client.IngestText(context.Background(), "docs", TextDocument{ID: "d1", Text: "hello world"})
	if err != nil {
		t.Fatalf("IngestText: %v", err)
	}
	if gotContentType != "application/json" {
		t.Errorf("Content-Type = %q, want application/json", gotContentType)
	}
	if !strings.Contains(gotBody, `"text":"hello world"`) {
		t.Errorf("body = %s, missing text", gotBody)
	}
	if result.Status != "ingested" || result.Chunks != 3 {
		t.Errorf("result = %+v", result)
	}
}

func TestIngestTextReturnsPartialResultInsteadOfError(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusInternalServerError)
		_, _ = w.Write([]byte(`{"documentId":"z","collection":"handbook","status":"partial","chunks":1,"chunksEmbedded":0,"bytes":11,"error":"no node is advertising embedding model 'no-such-embed-model'"}`))
	})
	defer closeSrv()

	result, err := client.IngestText(context.Background(), "handbook", TextDocument{ID: "z", Text: "x"})
	if err != nil {
		t.Fatalf("IngestText returned an error instead of the partial result: %v", err)
	}
	if result.Status != "partial" || result.DocumentID != "z" || result.ChunksEmbedded != 0 {
		t.Errorf("result = %+v", result)
	}
	if result.Error == "" {
		t.Errorf("Error field empty, want the hub's explanation")
	}
}

func TestIngestTextStillErrorsOnAGenuineErrorEnvelope(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusInternalServerError)
		_, _ = w.Write([]byte(`{"error":"internal server error"}`))
	})
	defer closeSrv()

	_, err := client.IngestText(context.Background(), "docs", TextDocument{ID: "d1", Text: "x"})
	if err == nil {
		t.Fatal("IngestText: want an error for a genuine error envelope, got nil")
	}
	var inferErr *Error
	if !errors.As(err, &inferErr) {
		t.Fatalf("error = %v, want *Error", err)
	}
	if inferErr.StatusCode != 500 {
		t.Errorf("StatusCode = %d, want 500", inferErr.StatusCode)
	}
}

func TestIngestFileSendsMultipartWithFileFieldLast(t *testing.T) {
	var gotFields []string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		mediaType, params, err := mime.ParseMediaType(r.Header.Get("Content-Type"))
		if err != nil || !strings.HasPrefix(mediaType, "multipart/") {
			t.Errorf("Content-Type = %q, want multipart/*", r.Header.Get("Content-Type"))
		}
		reader := multipart.NewReader(r.Body, params["boundary"])
		for {
			part, err := reader.NextPart()
			if err == io.EOF {
				break
			}
			if err != nil {
				t.Fatalf("reading multipart part: %v", err)
			}
			gotFields = append(gotFields, part.FormName())
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"documentId":"d1","collection":"docs","status":"ingested","chunks":1,"chunksEmbedded":1,"bytes":5}`))
	})
	defer closeSrv()

	_, err := client.IngestFile(context.Background(), "docs", FileDocument{
		ID: "d1", Filename: "note.txt", Body: bytes.NewBufferString("hello"),
		ContentType: "text/plain", Metadata: JSONDict{"source": "test"},
	})
	if err != nil {
		t.Fatalf("IngestFile: %v", err)
	}
	if len(gotFields) != 3 || gotFields[len(gotFields)-1] != "file" {
		t.Errorf("fields = %v, want [id metadata file] with file last", gotFields)
	}
}

func TestGetDocumentReturnsFalseOn404WithoutError(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	})
	defer closeSrv()

	_, ok, err := client.GetDocument(context.Background(), "docs", "missing")
	if err != nil {
		t.Fatalf("GetDocument returned an error on 404: %v", err)
	}
	if ok {
		t.Errorf("ok = true, want false on 404")
	}
}

func TestSearchErrorsOnMissingCollection(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusNotFound)
		_, _ = w.Write([]byte(`{"error":"collection 'missing' does not exist"}`))
	})
	defer closeSrv()

	_, err := client.Search(context.Background(), "missing", SearchRequest{Query: "q"})
	if err == nil {
		t.Fatal("Search: want an error for a missing collection, got nil (an empty corpus looks like a working one)")
	}
}

func TestSearchHitsStayInWireOrder(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"collection":"handbook","mode":"hybrid","hits":[` +
			`{"id":"a","score":0.01,"documentId":"policy.txt","text":"x"},` +
			`{"id":"b","score":0.03,"documentId":"onboarding","text":"y"}]}`))
	})
	defer closeSrv()

	result, err := client.Search(context.Background(), "handbook", SearchRequest{Query: "q"})
	if err != nil {
		t.Fatalf("Search: %v", err)
	}
	if len(result.Hits) != 2 || result.Hits[0].DocumentID != "policy.txt" || result.Hits[1].DocumentID != "onboarding" {
		t.Errorf("Hits = %+v, want wire order preserved despite the lower score coming first", result.Hits)
	}
}

func TestGetChunksIndexIsAString(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"collection":"handbook","documentId":"onboarding","chunks":[{"id":"c1","index":"0","page":null,"text":"chunk text"}]}`))
	})
	defer closeSrv()

	result, err := client.GetChunks(context.Background(), "handbook", "onboarding")
	if err != nil {
		t.Fatalf("GetChunks: %v", err)
	}
	if len(result.Chunks) != 1 || result.Chunks[0].Index != "0" {
		t.Errorf("Chunks = %+v, want Index == \"0\" (a string)", result.Chunks)
	}
}
