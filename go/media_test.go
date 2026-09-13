package inferhub

// Unit tests for media.go (go/v1.0.0) — multipart field ordering (dotnet D5: every field before
// the file part), always-requested verbose_json for Transcribe, the live-response shape for
// CreateSpeech, and the 404-is-not-an-error rule for image jobs (root rule 12).

import (
	"context"
	"io"
	"mime"
	"mime/multipart"
	"net/http"
	"strings"
	"testing"
)

func TestTranscribeAlwaysRequestsVerboseJson(t *testing.T) {
	var gotFormat string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		_, params, _ := mime.ParseMediaType(r.Header.Get("Content-Type"))
		reader := multipart.NewReader(r.Body, params["boundary"])
		for {
			part, err := reader.NextPart()
			if err == io.EOF {
				break
			}
			if part.FormName() == "response_format" {
				b, _ := io.ReadAll(part)
				gotFormat = string(b)
			}
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"text":"hi","segments":[]}`))
	})
	defer closeSrv()

	_, err := client.Transcribe(context.Background(), TranscriptionRequest{
		Model: "whisper", Audio: strings.NewReader("audio-bytes"), Filename: "a.wav",
		ResponseFormat: "srt", // ignored: Transcribe always forces verbose_json
	})
	if err != nil {
		t.Fatalf("Transcribe: %v", err)
	}
	if gotFormat != "verbose_json" {
		t.Errorf("response_format = %q, want verbose_json regardless of the request", gotFormat)
	}
}

func TestTranscriptionMultipartFieldsPrecedeFile(t *testing.T) {
	var gotFields []string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		_, params, _ := mime.ParseMediaType(r.Header.Get("Content-Type"))
		reader := multipart.NewReader(r.Body, params["boundary"])
		for {
			part, err := reader.NextPart()
			if err == io.EOF {
				break
			}
			gotFields = append(gotFields, part.FormName())
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"text":"hi","segments":[]}`))
	})
	defer closeSrv()

	_, err := client.Transcribe(context.Background(), TranscriptionRequest{
		Model: "whisper", Audio: strings.NewReader("x"), Filename: "a.wav", Language: "en",
	})
	if err != nil {
		t.Fatalf("Transcribe: %v", err)
	}
	if len(gotFields) == 0 || gotFields[len(gotFields)-1] != "file" {
		t.Errorf("fields = %v, want the file field last", gotFields)
	}
}

func TestTranscribeDocumentReturnsRawContent(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/vtt")
		_, _ = w.Write([]byte("WEBVTT\n\n00:00.000 --> 00:01.000\nhi\n"))
	})
	defer closeSrv()

	doc, err := client.TranscribeDocument(context.Background(), TranscriptionRequest{
		Model: "whisper", Audio: strings.NewReader("x"), Filename: "a.wav", ResponseFormat: "vtt",
	})
	if err != nil {
		t.Fatalf("TranscribeDocument: %v", err)
	}
	if !strings.HasPrefix(doc.Content, "WEBVTT") || doc.ContentType != "text/vtt" {
		t.Errorf("doc = %+v", doc)
	}
}

func TestCreateSpeechReturnsLiveResponseRegardlessOfStreamFormat(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "audio/wav")
		w.Header().Set("X-InferHub-Audio-Sample-Rate", "22050")
		_, _ = w.Write([]byte("RIFF...."))
	})
	defer closeSrv()

	audio, err := client.CreateSpeech(context.Background(), SpeechRequest{Model: "piper", Input: "hi"})
	if err != nil {
		t.Fatalf("CreateSpeech: %v", err)
	}
	defer audio.Response.Body.Close()
	if audio.SampleRate == nil || *audio.SampleRate != 22050 {
		t.Errorf("SampleRate = %v, want 22050", audio.SampleRate)
	}
	body, _ := io.ReadAll(audio.Response.Body)
	if string(body) != "RIFF...." {
		t.Errorf("body = %q", body)
	}
}

func TestStreamSpeechYieldsChunksAndStopsAtDone(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/event-stream")
		frames := "event: speech.audio.delta\ndata: {\"audio\":\"aGVsbG8=\"}\n\n" +
			"event: speech.audio.done\ndata: {\"usage\":{\"characters\":5}}\n\n"
		_, _ = w.Write([]byte(frames))
	})
	defer closeSrv()

	stream, err := client.StreamSpeech(context.Background(), SpeechRequest{Model: "piper", Input: "hello"})
	if err != nil {
		t.Fatalf("StreamSpeech: %v", err)
	}
	defer stream.Close()
	var chunks []SpeechChunk
	for stream.Next() {
		chunks = append(chunks, stream.Value())
	}
	if err := stream.Err(); err != nil {
		t.Fatalf("stream.Err(): %v", err)
	}
	if len(chunks) != 2 {
		t.Fatalf("len(chunks) = %d, want 2", len(chunks))
	}
	if string(chunks[0].Audio) != "hello" {
		t.Errorf("chunks[0].Audio = %q, want hello (decoded from base64)", chunks[0].Audio)
	}
	if chunks[1].Type != "speech.audio.done" {
		t.Errorf("chunks[1].Type = %q, want speech.audio.done", chunks[1].Type)
	}
}

func TestStreamSpeechErrorFrameThrowsOpenAIException(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/event-stream")
		_, _ = w.Write([]byte("event: speech.audio.error\ndata: {\"error\":{\"message\":\"boom\",\"code\":\"internal\"}}\n\n"))
	})
	defer closeSrv()

	stream, err := client.StreamSpeech(context.Background(), SpeechRequest{Model: "piper", Input: "hi"})
	if err != nil {
		t.Fatalf("StreamSpeech: %v", err)
	}
	defer stream.Close()
	for stream.Next() {
	}
	var inferErr *Error
	if stream.Err() == nil {
		t.Fatal("stream.Err() = nil, want an error for the speech.audio.error frame")
	}
	if inferErr, _ = stream.Err().(*Error); inferErr == nil || !inferErr.IsOpenAI() {
		t.Errorf("stream.Err() = %v, want a KindOpenAI *Error", stream.Err())
	}
}

func TestGetImageJobReturnsFalseOn404WithoutError(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	})
	defer closeSrv()

	_, ok, err := client.GetImageJob(context.Background(), "missing")
	if err != nil {
		t.Fatalf("GetImageJob returned an error on 404: %v", err)
	}
	if ok {
		t.Errorf("ok = true, want false on 404")
	}
}

func TestSubmitImageGenerationSendsOperationField(t *testing.T) {
	var gotBody string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		gotBody = string(b)
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"id":"j1","state":"queued","capability":"image","images":[]}`))
	})
	defer closeSrv()

	job, err := client.SubmitImageGeneration(context.Background(), ImageGenerationRequest{Model: "sd", Prompt: "a cat"})
	if err != nil {
		t.Fatalf("SubmitImageGeneration: %v", err)
	}
	if !strings.Contains(gotBody, `"operation":"generate"`) {
		t.Errorf("body = %s, missing operation:generate", gotBody)
	}
	if job.ID != "j1" || job.State != "queued" {
		t.Errorf("job = %+v", job)
	}
}

func TestWatchImageJobStopsAtTerminalState(t *testing.T) {
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/event-stream")
		frames := `data: {"id":"j1","state":"running","capability":"image","images":[]}` + "\n\n" +
			`data: {"id":"j1","state":"succeeded","capability":"image","images":[]}` + "\n\n"
		_, _ = w.Write([]byte(frames))
	})
	defer closeSrv()

	events, err := client.WatchImageJob(context.Background(), "j1")
	if err != nil {
		t.Fatalf("WatchImageJob: %v", err)
	}
	defer events.Close()
	var states []string
	for events.Next() {
		states = append(states, events.Value().State)
	}
	if len(states) != 2 || states[1] != "succeeded" {
		t.Errorf("states = %v, want [running succeeded]", states)
	}
}

func TestImageOptionsBecomeHeaders(t *testing.T) {
	var gotSteps, gotSeamRepair string
	client, closeSrv := newTestServer(t, func(w http.ResponseWriter, r *http.Request) {
		gotSteps = r.Header.Get("X-InferHub-Image-Steps")
		gotSeamRepair = r.Header.Get("X-InferHub-Image-Seam-Repair")
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"data":[]}`))
	})
	defer closeSrv()

	steps := 30
	_, err := client.GenerateImage(context.Background(), ImageGenerationRequest{
		Model: "sd", Prompt: "x", Options: &ImageOptions{Steps: &steps, SeamRepair: "diffuse"},
	})
	if err != nil {
		t.Fatalf("GenerateImage: %v", err)
	}
	if gotSteps != "30" || gotSeamRepair != "diffuse" {
		t.Errorf("steps=%q seamRepair=%q", gotSteps, gotSeamRepair)
	}
}
