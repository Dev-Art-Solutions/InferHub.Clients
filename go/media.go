package inferhub

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"io"
	"mime/multipart"
	"net/http"
	"strconv"
)

// Audio (/v1/audio/*) and images (/v1/images/* + /api/images/jobs) — go/v1.0.0. A separate file for
// the same reason corpus.go is (D2 in plans/phase-24-go-1-0.md): two modalities with multipart
// upload, read-once content and their own SSE framing would otherwise leave client.go covering
// four unrelated planes.

func (c *Client) multipartRequest(ctx context.Context, path string, extraHeaders map[string]string, writeFields func(*multipart.Writer) error) (*http.Response, error) {
	var buf bytes.Buffer
	writer := multipart.NewWriter(&buf)
	if err := writeFields(writer); err != nil {
		return nil, err
	}
	if err := writer.Close(); err != nil {
		return nil, fmt.Errorf("inferhub: closing multipart writer: %w", err)
	}
	target, err := c.resolve(path)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, target, &buf)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	for k, v := range c.buildHeaders() {
		req.Header[k] = v
	}
	for k, v := range extraHeaders {
		req.Header.Set(k, v)
	}
	req.Header.Set("Content-Type", writer.FormDataContentType())
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	return resp, nil
}

func writeFilePart(w *multipart.Writer, field, filename, contentType string, body io.Reader) error {
	part, err := w.CreatePart(fileHeaderFor(field, filename, contentType))
	if err != nil {
		return fmt.Errorf("inferhub: creating %s part: %w", field, err)
	}
	if _, err := io.Copy(part, body); err != nil {
		return fmt.Errorf("inferhub: writing %s content: %w", field, err)
	}
	return nil
}

func fileHeaderFor(field, filename, contentType string) map[string][]string {
	if contentType == "" {
		contentType = "application/octet-stream"
	}
	return map[string][]string{
		"Content-Disposition": {fmt.Sprintf(`form-data; name=%q; filename=%q`, field, filename)},
		"Content-Type":        {contentType},
	}
}

func intHeader(resp *http.Response, name string) *int {
	raw := resp.Header.Get(name)
	if raw == "" {
		return nil
	}
	value, err := strconv.Atoi(raw)
	if err != nil {
		return nil
	}
	return &value
}

// -- Audio ------------------------------------------------------------------------------------------

// TranscriptionRequest is the body of POST /v1/audio/transcriptions, multipart. Audio is read once
// and never copied into memory beyond what encoding/multipart itself buffers.
type TranscriptionRequest struct {
	Model          string
	Audio          io.Reader
	Filename       string
	ContentType    string
	Language       string
	Prompt         string
	Temperature    *float64
	ResponseFormat string
}

// Every field before the file part, always (dotnet phase-9 D5): above Tools:MaxStreamedBytes the
// hub routes from the leading fields and streams the bytes past them, so a field written after the
// file is a 400 on a large upload and silently fine on a small one.
func (c *Client) transcriptionRequest(ctx context.Context, req TranscriptionRequest, responseFormat string) (*http.Response, error) {
	if responseFormat == "" {
		responseFormat = req.ResponseFormat
	}
	if responseFormat == "" {
		responseFormat = "json"
	}
	return c.multipartRequest(ctx, "v1/audio/transcriptions", nil, func(w *multipart.Writer) error {
		if err := w.WriteField("model", req.Model); err != nil {
			return err
		}
		if req.Language != "" {
			if err := w.WriteField("language", req.Language); err != nil {
				return err
			}
		}
		if req.Prompt != "" {
			if err := w.WriteField("prompt", req.Prompt); err != nil {
				return err
			}
		}
		if req.Temperature != nil {
			if err := w.WriteField("temperature", strconv.FormatFloat(*req.Temperature, 'g', -1, 64)); err != nil {
				return err
			}
		}
		if err := w.WriteField("response_format", responseFormat); err != nil {
			return err
		}
		return writeFilePart(w, "file", req.Filename, req.ContentType, req.Audio)
	})
}

// TranscriptionSegment is one segment of a Transcription.
type TranscriptionSegment struct {
	ID    *int     `json:"id,omitempty"`
	Start *float64 `json:"start,omitempty"`
	End   *float64 `json:"end,omitempty"`
	Text  string   `json:"text"`
}

// Transcription is Transcribe's answer — always requested as verbose_json regardless of what the
// caller asked for (dotnet D6), because these are the fields a caller does something with. For
// text/srt/vtt use TranscribeDocument. Extra carries top-level fields this version does not model
// yet — the same escape hatch every other response type in this client gives (nested per-segment
// fields are not, since the hub's segment shape has been stable since v0.1.0's own extra decision).
type Transcription struct {
	Text     string                 `json:"text"`
	Language string                 `json:"language,omitempty"`
	Duration *float64               `json:"duration,omitempty"`
	Segments []TranscriptionSegment `json:"segments"`
	Extra    JSONDict               `json:"-"`
}

var transcriptionKnownFields = []string{"text", "language", "duration", "segments"}

// extractExtra re-parses body into a map and strips every key in known, for the escape-hatch Extra
// field a normal json.Unmarshal (which only fills tagged fields) cannot populate on its own.
func extractExtra(body []byte, known []string) JSONDict {
	var raw JSONDict
	if err := json.Unmarshal(body, &raw); err != nil {
		return JSONDict{}
	}
	for _, key := range known {
		delete(raw, key)
	}
	return raw
}

// Transcribe is POST /v1/audio/transcriptions, requested as verbose_json.
func (c *Client) Transcribe(ctx context.Context, req TranscriptionRequest) (Transcription, error) {
	var out Transcription
	resp, err := c.transcriptionRequest(ctx, req, "verbose_json")
	if err != nil {
		return out, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading transcription response: %w", err)
	}
	if err := json.Unmarshal(body, &out); err != nil {
		return out, fmt.Errorf("inferhub: decoding transcription response: %w", err)
	}
	out.Extra = extractExtra(body, transcriptionKnownFields)
	return out, nil
}

// TranscriptionDocument is POST /v1/audio/transcriptions rendered as text/srt/vtt and returned
// unaltered — the only honest thing to do with a subtitle file.
type TranscriptionDocument struct {
	Content     string
	ContentType string
	ServedBy    string
}

// TranscribeDocument is POST /v1/audio/transcriptions with req.ResponseFormat honored as-is
// (text/srt/vtt).
func (c *Client) TranscribeDocument(ctx context.Context, req TranscriptionRequest) (TranscriptionDocument, error) {
	var out TranscriptionDocument
	resp, err := c.transcriptionRequest(ctx, req, "")
	if err != nil {
		return out, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading transcription document: %w", err)
	}
	contentType := resp.Header.Get("Content-Type")
	if contentType == "" {
		contentType = "text/plain"
	}
	return TranscriptionDocument{Content: string(body), ContentType: contentType, ServedBy: readServedBy(resp)}, nil
}

// SpeechRequest is the body of POST /v1/audio/speech. StreamFormat is empty for the whole file at
// once, "audio" for a framed binary stream, or "sse" (forced by StreamSpeech, never set here
// directly) — only wav/pcm can stream; anything else is a 400 from the hub before a node is chosen.
type SpeechRequest struct {
	Model          string
	Input          string
	Voice          string
	ResponseFormat string
	StreamFormat   string
}

func (r SpeechRequest) toJSON() JSONDict {
	body := JSONDict{"model": r.Model, "input": r.Input}
	if r.Voice != "" {
		body["voice"] = r.Voice
	}
	if r.ResponseFormat != "" {
		body["response_format"] = r.ResponseFormat
	}
	if r.StreamFormat != "" {
		body["stream_format"] = r.StreamFormat
	}
	return body
}

// SpeechAudio is CreateSpeech's answer — the live *http.Response, read-once by nature of being an
// HTTP stream (root rule 7): the caller consumes Response.Body and this client never buffers it.
// SampleRate/Characters are nil unless the hub measured and stamped one (streaming synthesis only).
type SpeechAudio struct {
	Response    *http.Response
	ContentType string
	SampleRate  *int
	Characters  *int
	ServedBy    string
}

func (c *Client) speechRequest(ctx context.Context, req SpeechRequest) (*http.Response, error) {
	body, err := json.Marshal(req.toJSON())
	if err != nil {
		return nil, fmt.Errorf("inferhub: encoding speech request: %w", err)
	}
	httpReq, err := c.newRequest(ctx, http.MethodPost, "v1/audio/speech", body)
	if err != nil {
		return nil, err
	}
	resp, err := c.httpClient.Do(httpReq)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	return resp, nil
}

// CreateSpeech is POST /v1/audio/speech — hands back the live response whether or not
// StreamFormat is set (dotnet D2, load-bearing: not one line of caller code differs).
func (c *Client) CreateSpeech(ctx context.Context, req SpeechRequest) (SpeechAudio, error) {
	resp, err := c.speechRequest(ctx, req)
	if err != nil {
		return SpeechAudio{}, err
	}
	if err := raiseForStatus(resp); err != nil {
		return SpeechAudio{}, err
	}
	return SpeechAudio{
		Response: resp, ContentType: resp.Header.Get("Content-Type"),
		SampleRate: intHeader(resp, "X-InferHub-Audio-Sample-Rate"),
		Characters: intHeader(resp, "X-InferHub-Speech-Characters"),
		ServedBy:   readServedBy(resp),
	}, nil
}

// SpeechChunk is one SSE frame of StreamSpeech — a speech.audio.delta carrying Audio, or the
// terminal speech.audio.done carrying Usage and no audio.
type SpeechChunk struct {
	Type       string
	Audio      []byte
	Usage      JSONDict
	Characters *int
	ServedBy   string
	SampleRate *int
	Extra      JSONDict
}

// SpeechStream iterates StreamSpeech's SSE frames.
type SpeechStream struct {
	reader     *sseFrameReader
	servedBy   string
	sampleRate *int
	characters *int
	cur        SpeechChunk
	err        error
}

func (s *SpeechStream) Next() bool {
	if !s.reader.Next() {
		s.err = s.reader.Err()
		return false
	}
	frame := s.reader.Value()
	if frame.Event == "speech.audio.error" {
		errPayload, _ := frame.Data["error"].(JSONDict)
		message := ""
		code := ""
		if errPayload != nil {
			if m, ok := errPayload["message"].(string); ok {
				message = m
			}
			if c, ok := errPayload["code"].(string); ok {
				code = c
			}
		} else if m, ok := frame.Data["error"].(string); ok {
			message = m
		}
		raw, _ := json.Marshal(frame.Data)
		s.err = newOpenAIError(200, message, string(raw), nil, code, "", "")
		return false
	}
	chunk := SpeechChunk{Type: frame.Event, Characters: s.characters, ServedBy: s.servedBy, SampleRate: s.sampleRate, Extra: JSONDict{}}
	if chunk.Type == "" {
		if t, ok := frame.Data["type"].(string); ok {
			chunk.Type = t
		}
	}
	if b64, ok := frame.Data["audio"].(string); ok {
		if decoded, err := base64.StdEncoding.DecodeString(b64); err == nil {
			chunk.Audio = decoded
		}
	}
	if usage, ok := frame.Data["usage"].(JSONDict); ok {
		chunk.Usage = usage
	}
	for k, v := range frame.Data {
		if k != "audio" && k != "usage" && k != "type" {
			chunk.Extra[k] = v
		}
	}
	s.cur = chunk
	if chunk.Type == "speech.audio.done" {
		// One more Next() call will find EOF; nothing further to do — the caller's loop ends
		// naturally when Next() next returns false.
	}
	return true
}

func (s *SpeechStream) Value() SpeechChunk { return s.cur }
func (s *SpeechStream) Err() error         { return s.err }
func (s *SpeechStream) Close() error       { return s.reader.Close() }

// StreamSpeech is POST /v1/audio/speech with StreamFormat forced to "sse".
func (c *Client) StreamSpeech(ctx context.Context, req SpeechRequest) (*SpeechStream, error) {
	req.StreamFormat = "sse"
	resp, err := c.speechRequest(ctx, req)
	if err != nil {
		return nil, err
	}
	if err := raiseForStatus(resp); err != nil {
		return nil, err
	}
	return &SpeechStream{
		reader:     newSSEFrameReader(resp.Body),
		servedBy:   readServedBy(resp),
		sampleRate: intHeader(resp, "X-InferHub-Audio-Sample-Rate"),
		characters: intHeader(resp, "X-InferHub-Speech-Characters"),
	}, nil
}

// -- Images: synchronous OpenAI routes ---------------------------------------------------------------

// ImageOptions are the X-InferHub-Image-* extension headers — not body fields on the hub.
type ImageOptions struct {
	Steps          *int
	Guidance       *float64
	Seed           *int64
	Strength       *float64
	MaskConvention string
	SeamRepair     string
	Projection     string
}

func (o *ImageOptions) headers() map[string]string {
	h := map[string]string{}
	if o == nil {
		return h
	}
	if o.Steps != nil {
		h["X-InferHub-Image-Steps"] = strconv.Itoa(*o.Steps)
	}
	if o.Guidance != nil {
		h["X-InferHub-Image-Guidance"] = strconv.FormatFloat(*o.Guidance, 'g', -1, 64)
	}
	if o.Seed != nil {
		h["X-InferHub-Image-Seed"] = strconv.FormatInt(*o.Seed, 10)
	}
	if o.Strength != nil {
		h["X-InferHub-Image-Strength"] = strconv.FormatFloat(*o.Strength, 'g', -1, 64)
	}
	if o.MaskConvention != "" {
		h["X-InferHub-Image-Mask-Convention"] = o.MaskConvention
	}
	if o.SeamRepair != "" {
		h["X-InferHub-Image-Seam-Repair"] = o.SeamRepair
	}
	if o.Projection != "" {
		h["X-InferHub-Image-Projection"] = o.Projection
	}
	return h
}

// ImageGenerationRequest is the body of POST /v1/images/generations.
type ImageGenerationRequest struct {
	Model          string
	Prompt         string
	NegativePrompt string
	N              *int
	Size           string
	Seed           *int64
	ResponseFormat string
	Options        *ImageOptions
}

func (r ImageGenerationRequest) toJSON() JSONDict {
	body := JSONDict{"model": r.Model, "prompt": r.Prompt}
	if r.NegativePrompt != "" {
		body["negative_prompt"] = r.NegativePrompt
	}
	if r.N != nil {
		body["n"] = *r.N
	}
	if r.Size != "" {
		body["size"] = r.Size
	}
	if r.Seed != nil {
		body["seed"] = *r.Seed
	}
	if r.ResponseFormat != "" {
		body["response_format"] = r.ResponseFormat
	}
	return body
}

// ImageEditRequest is a picture and a prompt, multipart. With a mask, only the masked area is
// redrawn; without one, this is image-to-image.
type ImageEditRequest struct {
	Model            string
	Image            io.Reader
	ImageFilename    string
	Prompt           string
	Mask             io.Reader
	MaskFilename     string
	ImageContentType string
	MaskContentType  string
	Options          *ImageOptions
}

// ImageVariationRequest — no prompt, no mask: see ImageEditRequest's remarks.
type ImageVariationRequest struct {
	Model            string
	Image            io.Reader
	ImageFilename    string
	ImageContentType string
	Options          *ImageOptions
}

// ImageData is one generated image.
type ImageData struct {
	B64JSON         string   `json:"b64_json,omitempty"`
	Size            string   `json:"size,omitempty"`
	Seed            *int64   `json:"seed,omitempty"`
	Projection      string   `json:"projection,omitempty"`
	SeamDelta       *float64 `json:"seam_delta,omitempty"`
	SeamRepair      string   `json:"seam_repair,omitempty"`
	SeamDeltaBefore *float64 `json:"seam_delta_before,omitempty"`
	RevisedPrompt   string   `json:"revised_prompt,omitempty"`
}

var imageResponseKnownFields = []string{"created", "data", "prompt_augmented", "trigger", "warnings"}

// ImageResponse is POST /v1/images/generations|edits|variations's answer — pictures come back
// base64 in the envelope, because the hub stores nothing and so has no URL to serve. Extra carries
// top-level fields this version does not model yet.
type ImageResponse struct {
	Created         *int64      `json:"created,omitempty"`
	Data            []ImageData `json:"data"`
	PromptAugmented string      `json:"prompt_augmented,omitempty"`
	Trigger         string      `json:"trigger,omitempty"`
	Warnings        []string    `json:"warnings,omitempty"`
	Extra           JSONDict    `json:"-"`
}

func (c *Client) imageJSONRequest(ctx context.Context, path string, headers map[string]string, body JSONDict) (*http.Response, error) {
	encoded, err := json.Marshal(body)
	if err != nil {
		return nil, fmt.Errorf("inferhub: encoding %s request: %w", path, err)
	}
	req, err := c.newRequest(ctx, http.MethodPost, path, encoded)
	if err != nil {
		return nil, err
	}
	for k, v := range headers {
		req.Header.Set(k, v)
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	return resp, nil
}

func decodeImageResponse(resp *http.Response) (ImageResponse, error) {
	var out ImageResponse
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return out, err
	}
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return out, fmt.Errorf("inferhub: reading image response: %w", err)
	}
	if err := json.Unmarshal(body, &out); err != nil {
		return out, fmt.Errorf("inferhub: decoding image response: %w", err)
	}
	out.Extra = extractExtra(body, imageResponseKnownFields)
	return out, nil
}

// GenerateImage is POST /v1/images/generations.
func (c *Client) GenerateImage(ctx context.Context, req ImageGenerationRequest) (ImageResponse, error) {
	resp, err := c.imageJSONRequest(ctx, "v1/images/generations", req.Options.headers(), req.toJSON())
	if err != nil {
		return ImageResponse{}, err
	}
	return decodeImageResponse(resp)
}

// EditImage is POST /v1/images/edits, multipart.
func (c *Client) EditImage(ctx context.Context, req ImageEditRequest) (ImageResponse, error) {
	resp, err := c.multipartRequest(ctx, "v1/images/edits", req.Options.headers(), func(w *multipart.Writer) error {
		if err := w.WriteField("model", req.Model); err != nil {
			return err
		}
		if err := w.WriteField("prompt", req.Prompt); err != nil {
			return err
		}
		if err := writeFilePart(w, "image", req.ImageFilename, req.ImageContentType, req.Image); err != nil {
			return err
		}
		if req.Mask != nil {
			filename := req.MaskFilename
			if filename == "" {
				filename = "mask.png"
			}
			return writeFilePart(w, "mask", filename, req.MaskContentType, req.Mask)
		}
		return nil
	})
	if err != nil {
		return ImageResponse{}, err
	}
	return decodeImageResponse(resp)
}

// CreateImageVariation is POST /v1/images/variations, multipart.
func (c *Client) CreateImageVariation(ctx context.Context, req ImageVariationRequest) (ImageResponse, error) {
	resp, err := c.multipartRequest(ctx, "v1/images/variations", req.Options.headers(), func(w *multipart.Writer) error {
		if err := w.WriteField("model", req.Model); err != nil {
			return err
		}
		return writeFilePart(w, "image", req.ImageFilename, req.ImageContentType, req.Image)
	})
	if err != nil {
		return ImageResponse{}, err
	}
	return decodeImageResponse(resp)
}

// -- Images: the async job seam --------------------------------------------------------------------

// MediaJobOutput is one image slot of a MediaJob.
type MediaJobOutput struct {
	Index *int   `json:"index,omitempty"`
	URL   string `json:"url,omitempty"`
}

// MediaJob is the one job document both images and video jobs render through (dotnet D2).
type MediaJob struct {
	ID         string           `json:"id"`
	State      string           `json:"state"`
	Capability string           `json:"capability"`
	Step       *int             `json:"step,omitempty"`
	TotalSteps *int             `json:"totalSteps,omitempty"`
	Images     []MediaJobOutput `json:"images"`
	Extra      JSONDict         `json:"-"`
}

// MediaJobList is GET /api/images/jobs — client-scoped, never fleet-wide.
type MediaJobList struct {
	Jobs             []MediaJob `json:"jobs"`
	Queued           int        `json:"queued"`
	Active           int        `json:"active"`
	RetainedBytes    int64      `json:"retainedBytes"`
	RetentionSeconds int64      `json:"retentionSeconds"`
	Persistence      string     `json:"persistence"`
}

// ImageContent is GET /api/images/jobs/{id}/content/{index} — read once: the hub unlinks the bytes
// as they are read, so a retry is a 410. The caller consumes Response.Body.
type ImageContent struct {
	Response    *http.Response
	ContentType string
	Projection  string
	SeamRepair  string
}

func mediaJobFromJSON(raw JSONDict) MediaJob {
	encoded, _ := json.Marshal(raw)
	var job MediaJob
	_ = json.Unmarshal(encoded, &job)
	known := map[string]bool{"id": true, "state": true, "capability": true, "step": true, "totalSteps": true, "images": true}
	extra := JSONDict{}
	for k, v := range raw {
		if !known[k] {
			extra[k] = v
		}
	}
	job.Extra = extra
	return job
}

func (c *Client) submitImageJob(ctx context.Context, headers map[string]string, body JSONDict) (MediaJob, error) {
	resp, err := c.imageJSONRequest(ctx, "api/images/jobs", headers, body)
	if err != nil {
		return MediaJob{}, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return MediaJob{}, err
	}
	var raw JSONDict
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		return MediaJob{}, fmt.Errorf("inferhub: decoding media job response: %w", err)
	}
	return mediaJobFromJSON(raw), nil
}

// SubmitImageGeneration is POST /api/images/jobs, JSON — queues a generation and returns
// immediately. A 503 with Retry-After ("the fleet holds this model but nobody is doing this kind
// of work right now") returns *Error with Kind == KindOpenAI, distinct from a 404 (missing model).
func (c *Client) SubmitImageGeneration(ctx context.Context, req ImageGenerationRequest) (MediaJob, error) {
	body := req.toJSON()
	body["operation"] = "generate"
	return c.submitImageJob(ctx, req.Options.headers(), body)
}

// SubmitImageEdit is POST /api/images/jobs, multipart with operation=edit.
func (c *Client) SubmitImageEdit(ctx context.Context, req ImageEditRequest) (MediaJob, error) {
	resp, err := c.multipartRequest(ctx, "api/images/jobs", req.Options.headers(), func(w *multipart.Writer) error {
		if err := w.WriteField("model", req.Model); err != nil {
			return err
		}
		if err := w.WriteField("prompt", req.Prompt); err != nil {
			return err
		}
		if err := writeFilePart(w, "image", req.ImageFilename, req.ImageContentType, req.Image); err != nil {
			return err
		}
		if req.Mask != nil {
			filename := req.MaskFilename
			if filename == "" {
				filename = "mask.png"
			}
			if err := writeFilePart(w, "mask", filename, req.MaskContentType, req.Mask); err != nil {
				return err
			}
		}
		return w.WriteField("operation", "edit")
	})
	if err != nil {
		return MediaJob{}, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return MediaJob{}, err
	}
	var raw JSONDict
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		return MediaJob{}, fmt.Errorf("inferhub: decoding media job response: %w", err)
	}
	return mediaJobFromJSON(raw), nil
}

// SubmitImageVariation is POST /api/images/jobs, multipart with operation=variation.
func (c *Client) SubmitImageVariation(ctx context.Context, req ImageVariationRequest) (MediaJob, error) {
	resp, err := c.multipartRequest(ctx, "api/images/jobs", req.Options.headers(), func(w *multipart.Writer) error {
		if err := w.WriteField("model", req.Model); err != nil {
			return err
		}
		if err := writeFilePart(w, "image", req.ImageFilename, req.ImageContentType, req.Image); err != nil {
			return err
		}
		return w.WriteField("operation", "variation")
	})
	if err != nil {
		return MediaJob{}, err
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return MediaJob{}, err
	}
	var raw JSONDict
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		return MediaJob{}, fmt.Errorf("inferhub: decoding media job response: %w", err)
	}
	return mediaJobFromJSON(raw), nil
}

// ListImageJobs is GET /api/images/jobs — this client's jobs, client-scoped.
func (c *Client) ListImageJobs(ctx context.Context) (MediaJobList, error) {
	var out MediaJobList
	req, err := c.newRequest(ctx, http.MethodGet, "api/images/jobs", nil)
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
	var raw struct {
		Jobs             []JSONDict `json:"jobs"`
		Queued           int        `json:"queued"`
		Active           int        `json:"active"`
		RetainedBytes    int64      `json:"retainedBytes"`
		RetentionSeconds int64      `json:"retentionSeconds"`
		Persistence      string     `json:"persistence"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		return out, fmt.Errorf("inferhub: decoding image job list: %w", err)
	}
	jobs := make([]MediaJob, 0, len(raw.Jobs))
	for _, j := range raw.Jobs {
		jobs = append(jobs, mediaJobFromJSON(j))
	}
	out = MediaJobList{
		Jobs: jobs, Queued: raw.Queued, Active: raw.Active,
		RetainedBytes: raw.RetainedBytes, RetentionSeconds: raw.RetentionSeconds, Persistence: raw.Persistence,
	}
	return out, nil
}

// GetImageJob is GET /api/images/jobs/{id} — (MediaJob{}, false, nil) on 404 (not yours, or not
// there — the same body either way).
func (c *Client) GetImageJob(ctx context.Context, jobID string) (MediaJob, bool, error) {
	req, err := c.newRequest(ctx, http.MethodGet, "api/images/jobs/"+jobID, nil)
	if err != nil {
		return MediaJob{}, false, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return MediaJob{}, false, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode == http.StatusNotFound {
		return MediaJob{}, false, nil
	}
	if err := raiseForStatus(resp); err != nil {
		return MediaJob{}, false, err
	}
	var raw JSONDict
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		return MediaJob{}, false, fmt.Errorf("inferhub: decoding media job response: %w", err)
	}
	return mediaJobFromJSON(raw), true, nil
}

// ImageJobEvents iterates GET /api/images/jobs/{id}/events — one MediaJob per SSE frame. Walking
// away (not draining to done) does not cancel the job.
type ImageJobEvents struct {
	reader *sseFrameReader
	cur    MediaJob
	err    error
	done   bool
}

var terminalJobStates = map[string]bool{"succeeded": true, "failed": true, "cancelled": true, "expired": true}

func (e *ImageJobEvents) Next() bool {
	if e.done {
		return false
	}
	if !e.reader.Next() {
		e.err = e.reader.Err()
		e.done = true
		return false
	}
	e.cur = mediaJobFromJSON(e.reader.Value().Data)
	if terminalJobStates[e.cur.State] {
		e.done = true
	}
	return true
}

func (e *ImageJobEvents) Value() MediaJob { return e.cur }
func (e *ImageJobEvents) Err() error      { return e.err }
func (e *ImageJobEvents) Close() error    { return e.reader.Close() }

// WatchImageJob is GET /api/images/jobs/{id}/events.
func (c *Client) WatchImageJob(ctx context.Context, jobID string) (*ImageJobEvents, error) {
	req, err := c.newRequest(ctx, http.MethodGet, "api/images/jobs/"+jobID+"/events", nil)
	if err != nil {
		return nil, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("inferhub: %w", err)
	}
	if err := raiseForStatus(resp); err != nil {
		return nil, err
	}
	return &ImageJobEvents{reader: newSSEFrameReader(resp.Body)}, nil
}

// OpenImageContent is GET /api/images/jobs/{id}/content/{index} — read once. The caller consumes
// Response.Body. 410 job_expired on a repeat read, 409 job_not_ready, 404 image_not_found.
func (c *Client) OpenImageContent(ctx context.Context, jobID string, index int) (ImageContent, error) {
	req, err := c.newRequest(ctx, http.MethodGet, fmt.Sprintf("api/images/jobs/%s/content/%d", jobID, index), nil)
	if err != nil {
		return ImageContent{}, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return ImageContent{}, fmt.Errorf("inferhub: %w", err)
	}
	if err := raiseForStatus(resp); err != nil {
		return ImageContent{}, err
	}
	return ImageContent{
		Response: resp, ContentType: resp.Header.Get("Content-Type"),
		Projection: resp.Header.Get("X-InferHub-Image-Projection"),
		SeamRepair: resp.Header.Get("X-InferHub-Image-Seam-Repair"),
	}, nil
}

// CancelImageJob is DELETE /api/images/jobs/{id} — best effort; the returned job says what
// actually happened. A job already terminal is a 409 job_terminal.
func (c *Client) CancelImageJob(ctx context.Context, jobID string) (MediaJob, error) {
	req, err := c.newRequest(ctx, http.MethodDelete, "api/images/jobs/"+jobID, nil)
	if err != nil {
		return MediaJob{}, err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return MediaJob{}, fmt.Errorf("inferhub: %w", err)
	}
	defer resp.Body.Close()
	if err := raiseForStatus(resp); err != nil {
		return MediaJob{}, err
	}
	var raw JSONDict
	if err := json.NewDecoder(resp.Body).Decode(&raw); err != nil {
		return MediaJob{}, fmt.Errorf("inferhub: decoding media job response: %w", err)
	}
	return mediaJobFromJSON(raw), nil
}
