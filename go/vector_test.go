package inferhub

// Unit tests for vector.go (go/v0.2.0) — request shaping and the 404-is-not-an-error rule (root
// rule 12), mirroring js/test/client.test.ts's vector CRUD block and python 17's test_client.py.

import (
	"context"
	"io"
	"net/http"
	"strings"
	"testing"
)

func TestUpsertSendsIDVectorPayloadAndReturnsRecord(t *testing.T) {
	var gotPath, gotBody string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotPath = r.URL.Path
		b, _ := io.ReadAll(r.Body)
		gotBody = string(b)
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"id":"a1","vector":[0.1,0.2],"payload":{"k":"v"}}`))
	})
	defer closeSrv()

	rec, err := client.Upsert(context.Background(), "docs", VectorUpsert{
		ID: "a1", Vector: []float64{0.1, 0.2}, Payload: JSONDict{"k": "v"},
	})
	if err != nil {
		t.Fatalf("Upsert: %v", err)
	}
	if gotPath != "/api/vector/docs/upsert" {
		t.Errorf("path = %q, want /api/vector/docs/upsert", gotPath)
	}
	if !strings.Contains(gotBody, `"id":"a1"`) || !strings.Contains(gotBody, `"vector":[0.1,0.2]`) {
		t.Errorf("body = %s, missing id/vector", gotBody)
	}
	if rec.ID != "a1" || len(rec.Vector) != 2 {
		t.Errorf("record = %+v, want id a1 with a 2-vector", rec)
	}
}

func TestQueryDefaultsTopKToTen(t *testing.T) {
	var gotBody string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		gotBody = string(b)
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"matches":[{"id":"a1","score":0.9}]}`))
	})
	defer closeSrv()

	matches, err := client.Query(context.Background(), "docs", VectorQuery{Text: "hello"})
	if err != nil {
		t.Fatalf("Query: %v", err)
	}
	if !strings.Contains(gotBody, `"topK":10`) {
		t.Errorf("body = %s, want topK:10 default", gotBody)
	}
	if len(matches) != 1 || matches[0].ID != "a1" {
		t.Errorf("matches = %+v", matches)
	}
}

func TestRetrieveUsesRetrievePath(t *testing.T) {
	var gotPath string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotPath = r.URL.Path
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"matches":[]}`))
	})
	defer closeSrv()

	if _, err := client.Retrieve(context.Background(), "docs", VectorQuery{Text: "x"}); err != nil {
		t.Fatalf("Retrieve: %v", err)
	}
	if gotPath != "/api/vector/docs/retrieve" {
		t.Errorf("path = %q, want /api/vector/docs/retrieve", gotPath)
	}
}

func TestGetRecordReturnsFalseOn404WithoutError(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	})
	defer closeSrv()

	_, ok, err := client.GetRecord(context.Background(), "docs", "missing")
	if err != nil {
		t.Fatalf("GetRecord returned an error on 404: %v", err)
	}
	if ok {
		t.Errorf("ok = true, want false on 404")
	}
}

func TestDeleteRecordReturnsFalseOn404WithoutError(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	})
	defer closeSrv()

	deleted, err := client.DeleteRecord(context.Background(), "docs", "missing")
	if err != nil {
		t.Fatalf("DeleteRecord returned an error on 404: %v", err)
	}
	if deleted {
		t.Errorf("deleted = true, want false on 404")
	}
}

func TestDeleteRecordReturnsTrueOnSuccess(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
	})
	defer closeSrv()

	deleted, err := client.DeleteRecord(context.Background(), "docs", "a1")
	if err != nil {
		t.Fatalf("DeleteRecord: %v", err)
	}
	if !deleted {
		t.Errorf("deleted = false, want true")
	}
}
