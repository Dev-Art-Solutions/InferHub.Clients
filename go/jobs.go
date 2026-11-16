package inferhub

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"time"
)

// Text jobs — /api/jobs (hub v3.103+). A chat or a generate submitted as a job: the hub answers at
// once with an id, runs it the way /api/chat would, and keeps a status the caller, the node running
// it and the hub all update. For an answer that takes minutes — a big model, a model split across
// nodes, a full fleet — where one held connection is the wrong shape.
//
// Hub only: a solo node has no /api/jobs, and a submit there returns the 404 as an *Error.

// JobNode is the node a job was sent to. ReportedAt is when that node last said it is working on
// the job — it reports every couple of seconds, so a stale value means the node went quiet, not
// the model. Empty from a node older than v3.103.
type JobNode struct {
	ID         string `json:"id"`
	Name       string `json:"name,omitempty"`
	ReportedAt string `json:"reportedAt,omitempty"`
	Pieces     *int   `json:"pieces,omitempty"`
}

// JobGroupMember is one member of a split model's group: the layers it holds and how much of its
// share of the model the head has sent it (SentMiB of ExpectMiB) — the load, while a group starts.
// Both are nil for the head's own worker.
type JobGroupMember struct {
	Node      string `json:"node"`
	Layers    *int   `json:"layers,omitempty"`
	ExpectMiB *int64 `json:"expectMiB,omitempty"`
	SentMiB   *int64 `json:"sentMiB,omitempty"`
}

// JobGroup is present when the job's model is one model split across several nodes.
type JobGroup struct {
	Name    string           `json:"name"`
	Head    string           `json:"head,omitempty"`
	Ready   bool             `json:"ready"`
	State   string           `json:"state,omitempty"`
	Why     string           `json:"why,omitempty"`
	Members []JobGroupMember `json:"members"`
}

// LoadedFraction is how much of the members' shares has been sent, 0–1; ok is false when the group
// does not say (it is running, or its head is older than v3.102).
func (g JobGroup) LoadedFraction() (fraction float64, ok bool) {
	var expect, sent int64
	for _, m := range g.Members {
		if m.ExpectMiB == nil {
			continue
		}
		expect += *m.ExpectMiB
		if m.SentMiB != nil {
			sent += min(*m.SentMiB, *m.ExpectMiB)
		}
	}
	if expect <= 0 {
		return 0, false
	}
	return float64(sent) / float64(expect), true
}

// JobError is why a job failed: the HTTP status the blocking route would have answered, a code
// ("refused", "node_error", "node_lost", "deadline", "cancelled") and the sentence.
type JobError struct {
	Status            int    `json:"status,omitempty"`
	Code              string `json:"code,omitempty"`
	Message           string `json:"message,omitempty"`
	RetryAfterSeconds *int   `json:"retryAfterSeconds,omitempty"`
}

// Job statuses.
const (
	JobQueued    = "queued"
	JobRunning   = "running"
	JobSucceeded = "succeeded"
	JobFailed    = "failed"
	JobCancelled = "cancelled"
)

// Job is a job's status: names, counts and times — never the request or the answer. Stage is
// "waiting" (at the hub; Detail says for what), "sent", "preparing" (the node has it and its engine
// has produced nothing yet), "generating" or "done". Pieces counts the pieces of the answer
// received so far; Tokens is exact and arrives with the last one.
type Job struct {
	ID              string    `json:"id"`
	Kind            string    `json:"kind"`
	Model           string    `json:"model"`
	Status          string    `json:"status"`
	Stage           string    `json:"stage"`
	Detail          string    `json:"detail,omitempty"`
	CreatedAt       string    `json:"createdAt,omitempty"`
	StartedAt       string    `json:"startedAt,omitempty"`
	UpdatedAt       string    `json:"updatedAt,omitempty"`
	CompletedAt     string    `json:"completedAt,omitempty"`
	Node            *JobNode  `json:"node,omitempty"`
	ServedBy        string    `json:"servedBy,omitempty"`
	Attempts        int       `json:"attempts"`
	Pieces          int       `json:"pieces"`
	Tokens          *int      `json:"tokens,omitempty"`
	TokensPerSecond *float64  `json:"tokensPerSecond,omitempty"`
	Group           *JobGroup `json:"group,omitempty"`
	Error           *JobError `json:"error,omitempty"`
	Client          string    `json:"client,omitempty"`
}

// IsTerminal reports whether the job is finished: succeeded, failed or cancelled.
func (j Job) IsTerminal() bool {
	return j.Status == JobSucceeded || j.Status == JobFailed || j.Status == JobCancelled
}

// JobResult is a job's answer. Done is false while the job still runs — Chat/Generate then hold
// the text so far, which is what a caller that reconnects reads. Exactly one of them is set, by
// the job's kind.
type JobResult struct {
	Done     bool
	Chat     *ChatResponse
	Generate *GenerateResponse
	ServedBy string
}

// Text is the answer's text, whichever kind the job is.
func (r JobResult) Text() string {
	if r.Chat != nil && r.Chat.Message != nil {
		return r.Chat.Message.Content
	}
	if r.Generate != nil {
		return r.Generate.Response
	}
	return ""
}

func (c *Client) submitJob(ctx context.Context, kind string, request []byte) (Job, error) {
	var out Job
	body, err := json.Marshal(struct {
		Kind    string          `json:"kind"`
		Request json.RawMessage `json:"request"`
	}{kind, request})
	if err != nil {
		return out, fmt.Errorf("inferhub: encoding job: %w", err)
	}
	req, err := c.newRequest(ctx, http.MethodPost, "api/jobs", body)
	if err != nil {
		return out, err
	}
	return c.doJob(req)
}

func (c *Client) doJob(req *http.Request) (Job, error) {
	var out Job
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return out, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return out, fmt.Errorf("inferhub: decoding job: %w", err)
	}
	return out, nil
}

// SubmitChatJob is POST /api/jobs with a chat. It returns at once (202) with the job's status.
func (c *Client) SubmitChatJob(ctx context.Context, request ChatRequest) (Job, error) {
	body, err := request.marshalWithStream(true)
	if err != nil {
		return Job{}, fmt.Errorf("inferhub: encoding chat request: %w", err)
	}
	return c.submitJob(ctx, "chat", body)
}

// SubmitGenerateJob is POST /api/jobs with a generate.
func (c *Client) SubmitGenerateJob(ctx context.Context, request GenerateRequest) (Job, error) {
	body, err := request.marshalWithStream(true)
	if err != nil {
		return Job{}, fmt.Errorf("inferhub: encoding generate request: %w", err)
	}
	return c.submitJob(ctx, "generate", body)
}

// GetJob is GET /api/jobs/{id} — (Job{}, false, nil) for an id the hub does not hold for this
// client (not yours, or not there — the same 404 either way).
func (c *Client) GetJob(ctx context.Context, jobID string) (Job, bool, error) {
	req, err := c.newRequest(ctx, http.MethodGet, "api/jobs/"+jobID, nil)
	if err != nil {
		return Job{}, false, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return Job{}, false, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusNotFound {
		return Job{}, false, nil
	}
	if err := raiseForStatus(resp); err != nil {
		return Job{}, false, err
	}
	var out Job
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return Job{}, false, fmt.Errorf("inferhub: decoding job: %w", err)
	}
	return out, true, nil
}

// ListJobs is GET /api/jobs — this client's jobs, newest first.
func (c *Client) ListJobs(ctx context.Context) ([]Job, error) {
	req, err := c.newRequest(ctx, http.MethodGet, "api/jobs", nil)
	if err != nil {
		return nil, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return nil, err
	}
	var raw struct {
		Jobs []Job `json:"jobs"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		return nil, fmt.Errorf("inferhub: decoding job list: %w", err)
	}
	return raw.Jobs, nil
}

// GetJobResult is GET /api/jobs/{id}/result. Done is false, with the text so far, while the job
// runs. A failed job answers its own error status and a cancelled one a 409 — both are an *Error.
func (c *Client) GetJobResult(ctx context.Context, jobID string) (JobResult, error) {
	var out JobResult
	req, err := c.newRequest(ctx, http.MethodGet, "api/jobs/"+jobID+"/result", nil)
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
	var raw json.RawMessage
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		return out, fmt.Errorf("inferhub: decoding job result: %w", err)
	}
	var probe struct {
		Message json.RawMessage `json:"message"`
	}
	_ = json.Unmarshal(raw, &probe)
	out.Done = resp.StatusCode == http.StatusOK
	out.ServedBy = readServedBy(resp)
	if probe.Message != nil {
		var chat ChatResponse
		if err := json.Unmarshal(raw, &chat); err != nil {
			return out, fmt.Errorf("inferhub: decoding job result: %w", err)
		}
		chat.ServedBy = out.ServedBy
		out.Chat = &chat
		return out, nil
	}
	var generate GenerateResponse
	if err := json.Unmarshal(raw, &generate); err != nil {
		return out, fmt.Errorf("inferhub: decoding job result: %w", err)
	}
	generate.ServedBy = out.ServedBy
	out.Generate = &generate
	return out, nil
}

// CancelJob is DELETE /api/jobs/{id}. A running job is cancelled (the node is told to stop) and
// its status returned with true; a finished one is dropped now and (Job{}, false, nil) returned.
func (c *Client) CancelJob(ctx context.Context, jobID string) (Job, bool, error) {
	req, err := c.newRequest(ctx, http.MethodDelete, "api/jobs/"+jobID, nil)
	if err != nil {
		return Job{}, false, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return Job{}, false, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusNoContent || resp.StatusCode == http.StatusNotFound {
		return Job{}, false, nil
	}
	if err := raiseForStatus(resp); err != nil {
		return Job{}, false, err
	}
	var out Job
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return Job{}, false, fmt.Errorf("inferhub: decoding job: %w", err)
	}
	return out, true, nil
}

// JobEvents iterates GET /api/jobs/{id}/events — one Job per change, ending after the terminal
// one. Walking away does not cancel the job. Close it when done.
type JobEvents struct {
	reader *sseFrameReader
	cur    Job
	err    error
	done   bool
}

func (e *JobEvents) Next() bool {
	if e.done {
		return false
	}
	if !e.reader.Next() {
		e.err = e.reader.Err()
		e.done = true
		return false
	}
	raw, err := json.Marshal(e.reader.Value().Data)
	if err == nil {
		var job Job
		err = json.Unmarshal(raw, &job)
		e.cur = job
	}
	if err != nil {
		e.err = fmt.Errorf("inferhub: decoding job event: %w", err)
		e.done = true
		return false
	}
	if e.cur.IsTerminal() {
		e.done = true
	}
	return true
}

func (e *JobEvents) Value() Job   { return e.cur }
func (e *JobEvents) Err() error   { return e.err }
func (e *JobEvents) Close() error { return e.reader.Close() }

// WatchJob is GET /api/jobs/{id}/events. Use WaitForJob where a proxy buffers server-sent events.
func (c *Client) WatchJob(ctx context.Context, jobID string) (*JobEvents, error) {
	req, err := c.newRequest(ctx, http.MethodGet, "api/jobs/"+jobID+"/events", nil)
	if err != nil {
		return nil, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	if err := raiseForStatus(resp); err != nil {
		resp.Body.Close()
		return nil, err
	}
	return &JobEvents{reader: newSSEFrameReader(resp.Body)}, nil
}

// WaitForJob polls the status every pollInterval (one second when zero or less) until the job is
// finished and returns it. The context bounds the wait — the job keeps running when it expires. A
// job the hub no longer holds is an *Error with status 404.
func (c *Client) WaitForJob(ctx context.Context, jobID string, pollInterval time.Duration) (Job, error) {
	if pollInterval <= 0 {
		pollInterval = time.Second
	}
	for {
		job, found, err := c.GetJob(ctx, jobID)
		if err != nil {
			return Job{}, err
		}
		if !found {
			return Job{}, newError(http.StatusNotFound,
				"job '"+jobID+"' not found — it finished and its retention passed, the hub restarted, or it was never this client's",
				"", nil)
		}
		if job.IsTerminal() {
			return job, nil
		}
		select {
		case <-ctx.Done():
			return job, ctx.Err()
		case <-time.After(pollInterval):
		}
	}
}
