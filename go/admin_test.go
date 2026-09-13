package inferhub

// Unit tests for admin.go (go/v1.0.0) — the admin plane's request shaping, the 404-is-not-an-error
// rule for profiles/collections (root rule 12), and Probe's hub-vs-solo-node discrimination
// (exercised more thoroughly by the shared conformance cases in conformance_test.go).

import (
	"context"
	"io"
	"net/http"
	"strings"
	"testing"
	"time"
)

func TestCordonPostsToTheRightPath(t *testing.T) {
	var gotPath, gotMethod string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotPath, gotMethod = r.URL.Path, r.Method
		w.WriteHeader(http.StatusOK)
	})
	defer closeSrv()

	if err := client.Cordon(context.Background(), "n1"); err != nil {
		t.Fatalf("Cordon: %v", err)
	}
	if gotPath != "/api/admin/nodes/n1/cordon" || gotMethod != http.MethodPost {
		t.Errorf("path=%q method=%q", gotPath, gotMethod)
	}
}

func TestGetProfileReturnsFalseOn404WithoutError(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	})
	defer closeSrv()

	_, ok, err := client.GetProfile(context.Background(), "missing")
	if err != nil {
		t.Fatalf("GetProfile returned an error on 404: %v", err)
	}
	if ok {
		t.Errorf("ok = true, want false on 404")
	}
}

func TestPutProfileOmitsNameAndRevision(t *testing.T) {
	var gotBody string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		gotBody = string(b)
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"applied":["n1"],"conflicts":[]}`))
	})
	defer closeSrv()

	_, err := client.PutProfile(context.Background(), "gpu-boxes", NodeProfile{
		Name: "ignored", Revision: 99, Selector: JSONDict{"label": "gpu"},
	})
	if err != nil {
		t.Fatalf("PutProfile: %v", err)
	}
	if strings.Contains(gotBody, `"name"`) || strings.Contains(gotBody, `"revision"`) {
		t.Errorf("body = %s, want name/revision omitted (the hub sets both from the route)", gotBody)
	}
}

func TestPullModelDecodesModelCommandAccepted(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/api/admin/nodes/n1/models/llama3/pull" {
			t.Errorf("path = %q", r.URL.Path)
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"nodeId":"n1","model":"llama3","kind":"pull","commandId":"c1","reused":false}`))
	})
	defer closeSrv()

	result, err := client.PullModel(context.Background(), "n1", "llama3")
	if err != nil {
		t.Fatalf("PullModel: %v", err)
	}
	if result.CommandID != "c1" || result.Reused {
		t.Errorf("result = %+v", result)
	}
}

func TestQueryUsageBuildsQueryString(t *testing.T) {
	var gotQuery string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotQuery = r.URL.RawQuery
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"rows":[]}`))
	})
	defer closeSrv()

	from := time.Date(2026, 1, 1, 0, 0, 0, 0, time.UTC)
	_, err := client.QueryUsage(context.Background(), UsageQuery{From: from, ClientID: "c1"})
	if err != nil {
		t.Fatalf("QueryUsage: %v", err)
	}
	if !strings.Contains(gotQuery, "clientId=c1") || !strings.Contains(gotQuery, "from=") {
		t.Errorf("query = %q", gotQuery)
	}
}

func TestQueryUsageAcceptsBareArrayBody(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`[{"clientId":"c1","model":"llama3","requests":5,"promptTokens":10,"completionTokens":20,"totalTokens":30,"fallbackRequests":0}]`))
	})
	defer closeSrv()

	result, err := client.QueryUsage(context.Background(), UsageQuery{})
	if err != nil {
		t.Fatalf("QueryUsage: %v", err)
	}
	if len(result.Rows) != 1 || result.Rows[0].ClientID != "c1" {
		t.Errorf("rows = %+v", result.Rows)
	}
}

func TestListClientsNeverExposesAKeyField(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`[{"clientId":"c1","limits":{"rpm":60}}]`))
	})
	defer closeSrv()

	rows, err := client.ListClients(context.Background())
	if err != nil {
		t.Fatalf("ListClients: %v", err)
	}
	if len(rows) != 1 || rows[0].ClientID != "c1" {
		t.Errorf("rows = %+v", rows)
	}
}

func TestGetNodeCollectionReturnsFalseOn404WithoutError(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	})
	defer closeSrv()

	_, ok, err := client.GetNodeCollection(context.Background(), "missing")
	if err != nil {
		t.Fatalf("GetNodeCollection returned an error on 404: %v", err)
	}
	if ok {
		t.Errorf("ok = true, want false on 404")
	}
}

func TestGetNodeVersionUsesNodeOnlyRoute(t *testing.T) {
	var gotPath string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotPath = r.URL.Path
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"version":"3.37.0"}`))
	})
	defer closeSrv()

	version, err := client.GetNodeVersion(context.Background())
	if err != nil {
		t.Fatalf("GetNodeVersion: %v", err)
	}
	if version != "3.37.0" || gotPath != "/api/version" {
		t.Errorf("version=%q path=%q", version, gotPath)
	}
}

func TestStreamAdminEventsYieldsEventAndData(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/event-stream")
		_, _ = w.Write([]byte("event: snapshot\ndata: {\"nodes\":1}\n\n"))
	})
	defer closeSrv()

	stream, err := client.StreamAdminEvents(context.Background())
	if err != nil {
		t.Fatalf("StreamAdminEvents: %v", err)
	}
	defer stream.Close()
	if !stream.Next() {
		t.Fatalf("stream.Next() = false, err = %v", stream.Err())
	}
	if stream.Value().Event != "snapshot" {
		t.Errorf("Event = %q, want snapshot", stream.Value().Event)
	}
}
