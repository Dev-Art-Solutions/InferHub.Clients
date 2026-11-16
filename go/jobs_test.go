package inferhub

// Text jobs (hub v3.103+). The status bodies and the result headers are RECORDED from a real
// coordinator and a real node over Ollama (qwen2.5:0.5b) on 2026-11-16. The group body is DERIVED
// from the hub's serializer (JobEndpoints.Describe) — no split-model group was running when these
// were recorded — and so is the SSE framing.

import (
	"context"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
	"time"
)

const jobID = "8755d357-7f7a-493b-984b-09eeb1c3797e"

const jobQueued = `{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b","status":"queued","stage":"waiting","createdAt":"2026-11-16T11:40:16.2677184+00:00","updatedAt":"2026-11-16T11:40:16.2677184+00:00","attempts":0,"pieces":0}`

const jobPreparing = `{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b","status":"running","stage":"preparing","createdAt":"2026-11-16T11:40:16.2677184+00:00","startedAt":"2026-11-16T11:40:16.2950626+00:00","updatedAt":"2026-11-16T11:40:18.3304721+00:00","node":{"id":"d2b53940-e3b4-4b37-855d-536e9a4fe10e","name":"live150-node","reportedAt":"2026-11-16T11:40:18.3304721+00:00","pieces":0},"attempts":1,"pieces":0}`

const jobSucceeded = `{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"qwen2.5:0.5b","status":"succeeded","stage":"done","createdAt":"2026-11-16T11:40:16.2677184+00:00","startedAt":"2026-11-16T11:40:16.2950626+00:00","updatedAt":"2026-11-16T11:40:33.342876+00:00","completedAt":"2026-11-16T11:40:33.342876+00:00","node":{"id":"d2b53940-e3b4-4b37-855d-536e9a4fe10e","name":"live150-node","reportedAt":"2026-11-16T11:40:32.4983246+00:00","pieces":1},"attempts":1,"pieces":300,"tokens":300,"tokensPerSecond":340.23}`

const jobPartialResult = `{"model":"qwen2.5:0.5b","message":{"role":"assistant","content":""},"done":false}`

const jobResult = `{"model":"qwen2.5:0.5b","created_at":"2026-11-16T11:40:33.3396733Z","message":{"role":"assistant","content":"Ah, the lighthouse keeper."},"done":true,"done_reason":"length","eval_count":300}`

// Derived, not recorded.
const jobWithGroup = `{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"wizardlm-70b-split","status":"running","stage":"preparing","detail":"group starting: 18420 of 25022 MiB sent to its members","attempts":1,"pieces":0,"group":{"name":"wizardlm-70b-split","head":"shard-a","ready":false,"state":"starting","members":[{"node":"shard-a","layers":27},{"node":"shard-b","layers":27,"expectMiB":12511,"sentMiB":9210},{"node":"shard-c","layers":27,"expectMiB":12511,"sentMiB":9210}]}}`

const jobFailed = `{"id":"8755d357-7f7a-493b-984b-09eeb1c3797e","kind":"chat","model":"llama3","status":"failed","stage":"done","attempts":1,"pieces":4,"error":{"status":502,"code":"node_lost","message":"node 'gpu-1' disappeared while this job was running; it was not retried"}}`

func jobServer(t *testing.T, status int, body string, headers map[string]string) (*Client, func(), *[]*http.Request, *[]string) {
	t.Helper()
	var requests []*http.Request
	var bodies []string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		requests = append(requests, r)
		bodies = append(bodies, string(b))
		w.Header().Set("Content-Type", "application/json")
		for k, v := range headers {
			w.Header().Set(k, v)
		}
		w.WriteHeader(status)
		_, _ = w.Write([]byte(body))
	})
	return client, closeSrv, &requests, &bodies
}

func TestSubmitChatJobWrapsTheRequestAndReturnsTheStatusAtOnce(t *testing.T) {
	client, closeSrv, requests, bodies := jobServer(t, http.StatusAccepted, jobQueued, nil)
	defer closeSrv()

	job, err := client.SubmitChatJob(context.Background(), ChatRequest{
		Model: "qwen2.5:0.5b", Messages: []ChatMessage{{Role: "user", Content: "hi"}},
	})
	if err != nil {
		t.Fatalf("SubmitChatJob: %v", err)
	}
	if (*requests)[0].URL.Path != "/api/jobs" || (*requests)[0].Method != http.MethodPost {
		t.Errorf("request = %s %s", (*requests)[0].Method, (*requests)[0].URL.Path)
	}
	sent := (*bodies)[0]
	for _, want := range []string{`"kind":"chat"`, `"request":{`, `"model":"qwen2.5:0.5b"`, `"content":"hi"`} {
		if !strings.Contains(sent, want) {
			t.Errorf("body = %s, missing %s", sent, want)
		}
	}
	if job.ID != jobID || job.Status != JobQueued || job.Stage != "waiting" || job.IsTerminal() || job.Node != nil {
		t.Errorf("job = %+v", job)
	}
}

func TestSubmitGenerateJobNamesItsKind(t *testing.T) {
	client, closeSrv, _, bodies := jobServer(t, http.StatusAccepted, jobQueued, nil)
	defer closeSrv()
	if _, err := client.SubmitGenerateJob(context.Background(), GenerateRequest{Model: "qwen2.5:0.5b", Prompt: "hi"}); err != nil {
		t.Fatalf("SubmitGenerateJob: %v", err)
	}
	if !strings.Contains((*bodies)[0], `"kind":"generate"`) {
		t.Errorf("body = %s", (*bodies)[0])
	}
}

func TestARunningJobCarriesTheNodesOwnReport(t *testing.T) {
	client, closeSrv, _, _ := jobServer(t, http.StatusOK, jobPreparing, nil)
	defer closeSrv()
	job, found, err := client.GetJob(context.Background(), jobID)
	if err != nil || !found {
		t.Fatalf("GetJob: %v found=%v", err, found)
	}
	if job.Status != JobRunning || job.Stage != "preparing" || job.Node == nil ||
		job.Node.Name != "live150-node" || job.Node.ReportedAt != "2026-11-16T11:40:18.3304721+00:00" {
		t.Errorf("job = %+v node = %+v", job, job.Node)
	}
}

func TestAFinishedJobHasExactTokens(t *testing.T) {
	client, closeSrv, _, _ := jobServer(t, http.StatusOK, jobSucceeded, nil)
	defer closeSrv()
	job, _, err := client.GetJob(context.Background(), jobID)
	if err != nil {
		t.Fatal(err)
	}
	if !job.IsTerminal() || job.Tokens == nil || *job.Tokens != 300 || job.TokensPerSecond == nil || *job.TokensPerSecond != 340.23 {
		t.Errorf("job = %+v", job)
	}
}

func TestASplitModelsJobSaysHowFarEachMemberIs(t *testing.T) {
	client, closeSrv, _, _ := jobServer(t, http.StatusOK, jobWithGroup, nil)
	defer closeSrv()
	job, _, err := client.GetJob(context.Background(), jobID)
	if err != nil {
		t.Fatal(err)
	}
	if job.Group == nil || job.Group.State != "starting" || job.Group.Ready || len(job.Group.Members) != 3 {
		t.Fatalf("group = %+v", job.Group)
	}
	if job.Group.Members[0].ExpectMiB != nil || *job.Group.Members[1].SentMiB != 9210 {
		t.Errorf("members = %+v", job.Group.Members)
	}
	fraction, ok := job.Group.LoadedFraction()
	if !ok || fraction < 0.736 || fraction > 0.737 {
		t.Errorf("LoadedFraction = %v, %v", fraction, ok)
	}
	if !strings.HasPrefix(job.Detail, "group starting") {
		t.Errorf("detail = %q", job.Detail)
	}
}

func TestAnUnknownJobIsNotFoundNotAnError(t *testing.T) {
	client, closeSrv, _, _ := jobServer(t, http.StatusNotFound, `{"error":"job not found"}`, nil)
	defer closeSrv()
	_, found, err := client.GetJob(context.Background(), jobID)
	if err != nil || found {
		t.Errorf("GetJob = found %v, err %v", found, err)
	}
}

func TestTheResultOfARunningJobIsTheTextSoFar(t *testing.T) {
	client, closeSrv, _, _ := jobServer(t, http.StatusAccepted, jobPartialResult, nil)
	defer closeSrv()
	result, err := client.GetJobResult(context.Background(), jobID)
	if err != nil {
		t.Fatal(err)
	}
	if result.Done || result.Text() != "" || result.Chat == nil {
		t.Errorf("result = %+v", result)
	}
}

func TestTheResultOfAFinishedJobIsTheChatAnswerWithWhoServedIt(t *testing.T) {
	client, closeSrv, requests, _ := jobServer(t, http.StatusOK, jobResult, map[string]string{"X-InferHub-Served-By": "node"})
	defer closeSrv()
	result, err := client.GetJobResult(context.Background(), jobID)
	if err != nil {
		t.Fatal(err)
	}
	if (*requests)[0].URL.Path != "/api/jobs/"+jobID+"/result" {
		t.Errorf("path = %s", (*requests)[0].URL.Path)
	}
	if !result.Done || result.Text() != "Ah, the lighthouse keeper." || result.ServedBy != "node" || result.Generate != nil {
		t.Errorf("result = %+v", result)
	}
}

func TestTheResultOfAFailedJobIsItsOwnStatus(t *testing.T) {
	client, closeSrv, _, _ := jobServer(t, http.StatusBadGateway, `{"error":"node 'gpu-1' disappeared"}`, nil)
	defer closeSrv()
	_, err := client.GetJobResult(context.Background(), jobID)
	var apiErr *Error
	if !errors.As(err, &apiErr) || apiErr.StatusCode != http.StatusBadGateway {
		t.Errorf("err = %v", err)
	}
}

func TestAFailedStatusIsReadNotReturnedAsAnError(t *testing.T) {
	client, closeSrv, _, _ := jobServer(t, http.StatusOK, jobFailed, nil)
	defer closeSrv()
	job, _, err := client.GetJob(context.Background(), jobID)
	if err != nil {
		t.Fatal(err)
	}
	if job.Status != JobFailed || job.Error == nil || job.Error.Code != "node_lost" || job.Error.Status != 502 {
		t.Errorf("job = %+v error = %+v", job, job.Error)
	}
}

func TestCancelReturnsTheStatusAndDroppingAFinishedJobReturnsFalse(t *testing.T) {
	client, closeSrv, requests, _ := jobServer(t, http.StatusAccepted, strings.Replace(jobFailed, `"failed"`, `"cancelled"`, 1), nil)
	job, cancelled, err := client.CancelJob(context.Background(), jobID)
	closeSrv()
	if err != nil || !cancelled || job.Status != JobCancelled || (*requests)[0].Method != http.MethodDelete {
		t.Errorf("CancelJob = %+v, %v, %v", job, cancelled, err)
	}

	client, closeSrv, _, _ = jobServer(t, http.StatusNoContent, "", nil)
	defer closeSrv()
	if _, cancelled, err := client.CancelJob(context.Background(), jobID); err != nil || cancelled {
		t.Errorf("CancelJob on a finished job = %v, %v", cancelled, err)
	}
}

func TestListJobsReadsTheEnvelope(t *testing.T) {
	client, closeSrv, _, _ := jobServer(t, http.StatusOK, `{"jobs":[`+jobSucceeded+`,`+jobPreparing+`]}`, nil)
	defer closeSrv()
	jobs, err := client.ListJobs(context.Background())
	if err != nil || len(jobs) != 2 || jobs[0].Status != JobSucceeded || jobs[1].Status != JobRunning {
		t.Errorf("ListJobs = %+v, %v", jobs, err)
	}
}

func TestWatchYieldsEachStatusAndStopsAtTheTerminalOne(t *testing.T) {
	stream := "event: status\ndata: " + jobPreparing + "\n\n: waiting\n\n" +
		"event: status\ndata: " + jobSucceeded + "\n\nevent: status\ndata: " + jobQueued + "\n\n"
	client, closeSrv, requests, _ := jobServer(t, http.StatusOK, stream, map[string]string{"Content-Type": "text/event-stream"})
	defer closeSrv()

	events, err := client.WatchJob(context.Background(), jobID)
	if err != nil {
		t.Fatalf("WatchJob: %v", err)
	}
	defer events.Close()
	var seen []string
	for events.Next() {
		seen = append(seen, events.Value().Status)
	}
	if events.Err() != nil || strings.Join(seen, ",") != "running,succeeded" {
		t.Errorf("seen = %v, err = %v", seen, events.Err())
	}
	if (*requests)[0].URL.Path != "/api/jobs/"+jobID+"/events" {
		t.Errorf("path = %s", (*requests)[0].URL.Path)
	}
}

func TestWaitPollsUntilTheJobIsFinished(t *testing.T) {
	bodies := []string{jobQueued, jobPreparing, jobSucceeded}
	calls := 0
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		body := bodies[min(calls, len(bodies)-1)]
		calls++
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(body))
	})
	defer closeSrv()

	job, err := client.WaitForJob(context.Background(), jobID, time.Millisecond)
	if err != nil || job.Status != JobSucceeded || calls != 3 {
		t.Errorf("WaitForJob = %+v, %v after %d calls", job, err, calls)
	}
}

func TestWaitStopsWithTheContextAndAJobThatIsGoneIsA404(t *testing.T) {
	client, closeSrv, _, _ := jobServer(t, http.StatusOK, jobPreparing, nil)
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Millisecond)
	job, err := client.WaitForJob(ctx, jobID, 5*time.Millisecond)
	cancel()
	closeSrv()
	if !errors.Is(err, context.DeadlineExceeded) || job.Status != JobRunning {
		t.Errorf("WaitForJob = %+v, %v", job, err)
	}

	client, closeSrv, _, _ = jobServer(t, http.StatusNotFound, `{"error":"job not found"}`, nil)
	defer closeSrv()
	_, err = client.WaitForJob(context.Background(), jobID, time.Millisecond)
	var apiErr *Error
	if !errors.As(err, &apiErr) || apiErr.StatusCode != http.StatusNotFound {
		t.Errorf("err = %v", err)
	}
}
