/**
 * Audio (`/v1/audio/*`) and images (`/v1/images/*` + `/api/images/jobs`) — a separate module for
 * the same reason `_corpus.ts` is (phase 20 D3): two modalities with multipart upload, read-once
 * content and their own SSE framing would otherwise leave `client.ts` covering four unrelated
 * planes. Each function takes the client's own `request()` (phase-21 D1), so there is nothing
 * here that touches `fetch` directly.
 */

import { raiseForStatus, readServedBy } from "./_base.js";
import type { Requester } from "./_corpus.js";
import { readSseFrames } from "./_stream.js";
import { InferHubError, InferHubOpenAiException } from "./errors.js";
import type {
  ImageContent,
  ImageData,
  ImageEditRequest,
  ImageGenerationRequest,
  ImageOptions,
  ImageResponse,
  ImageVariationRequest,
  JsonDict,
  MediaJob,
  MediaJobList,
  MediaJobOutput,
  SpeechAudio,
  SpeechChunk,
  SpeechRequest,
  Transcription,
  TranscriptionDocument,
  TranscriptionRequest,
  TranscriptionSegment,
} from "./types.js";

function intHeader(response: Response, name: string): number | undefined {
  const raw = response.headers.get(name);
  if (raw === null) return undefined;
  const value = Number(raw);
  return Number.isNaN(value) ? undefined : value;
}

function blobWithType(blob: Blob, contentType?: string): Blob {
  return contentType && contentType !== blob.type ? blob.slice(0, blob.size, contentType) : blob;
}

function base64ToBytes(b64: string): Uint8Array {
  const binary = atob(b64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}

// -- Audio --------------------------------------------------------------------------------------

/** Every field before the file part, always (dotnet phase-9 D5): above `Tools:MaxStreamedBytes`
 * the hub routes from the leading fields and streams the bytes past them, so a field written
 * after `file` is a `400` on a large upload and silently fine on a small one. */
function transcriptionFormData(request: TranscriptionRequest, responseFormat?: string): FormData {
  const form = new FormData();
  form.append("model", request.model);
  if (request.language !== undefined) form.append("language", request.language);
  if (request.prompt !== undefined) form.append("prompt", request.prompt);
  if (request.temperature !== undefined) form.append("temperature", String(request.temperature));
  form.append("response_format", responseFormat ?? request.responseFormat ?? "json");
  form.append("file", blobWithType(request.audio, request.contentType), request.filename);
  return form;
}

function transcriptionSegmentFromJson(data: JsonDict): TranscriptionSegment {
  const known = new Set(["id", "start", "end", "text"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    id: data.id as number | undefined,
    start: data.start as number | undefined,
    end: data.end as number | undefined,
    text: (data.text as string) ?? "",
    extra,
  };
}

function transcriptionFromJson(data: JsonDict): Transcription {
  const known = new Set(["text", "language", "duration", "segments"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    text: (data.text as string) ?? "",
    language: data.language as string | undefined,
    duration: data.duration as number | undefined,
    segments: ((data.segments as JsonDict[] | undefined) ?? []).map(transcriptionSegmentFromJson),
    extra,
  };
}

/** `POST /v1/audio/transcriptions` — always asks for `verbose_json` regardless of
 * `request.responseFormat` (dotnet D6). */
export async function transcribe(
  request: Requester,
  req: TranscriptionRequest,
): Promise<Transcription> {
  const response = await request("v1/audio/transcriptions", {
    method: "POST",
    body: transcriptionFormData(req, "verbose_json"),
  });
  await raiseForStatus(response);
  return transcriptionFromJson((await response.json()) as JsonDict);
}

/** `POST /v1/audio/transcriptions` rendered as `text`/`srt`/`vtt` and returned unaltered. */
export async function transcribeDocument(
  request: Requester,
  req: TranscriptionRequest,
): Promise<TranscriptionDocument> {
  const response = await request("v1/audio/transcriptions", {
    method: "POST",
    body: transcriptionFormData(req),
  });
  await raiseForStatus(response);
  return {
    content: await response.text(),
    contentType: response.headers.get("Content-Type") ?? "text/plain",
    servedBy: readServedBy(response),
  };
}

function speechRequestToJson(req: SpeechRequest): JsonDict {
  const body: JsonDict = { model: req.model, input: req.input };
  if (req.voice !== undefined) body.voice = req.voice;
  if (req.responseFormat !== undefined) body.response_format = req.responseFormat;
  if (req.streamFormat !== undefined) body.stream_format = req.streamFormat;
  return body;
}

function speechAudioFromResponse(response: Response): SpeechAudio {
  return {
    response,
    contentType: response.headers.get("Content-Type") ?? undefined,
    sampleRate: intHeader(response, "X-InferHub-Audio-Sample-Rate"),
    characters: intHeader(response, "X-InferHub-Speech-Characters"),
    servedBy: readServedBy(response),
  };
}

/** `POST /v1/audio/speech` — hands back the live response whether or not `streamFormat` is set
 * (dotnet D2, load-bearing: not one line of caller code differs). The caller consumes
 * `result.response.body`/`.arrayBuffer()`. */
export async function createSpeech(request: Requester, req: SpeechRequest): Promise<SpeechAudio> {
  const response = await request("v1/audio/speech", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(speechRequestToJson(req)),
  });
  if (!response.ok) {
    await raiseForStatus(response);
  }
  return speechAudioFromResponse(response);
}

/** `POST /v1/audio/speech` with `streamFormat` forced to `"sse"` — yields one `SpeechChunk` per
 * `speech.audio.delta`/`speech.audio.done` frame. A `speech.audio.error` frame throws
 * `InferHubOpenAiException`. */
export async function* streamSpeech(
  request: Requester,
  req: SpeechRequest,
): AsyncGenerator<SpeechChunk> {
  const response = await request("v1/audio/speech", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(speechRequestToJson({ ...req, streamFormat: "sse" })),
  });
  await raiseForStatus(response);
  if (!response.body) {
    throw new InferHubError(response.status, "InferHub streaming response had no body.");
  }
  const servedBy = readServedBy(response);
  const sampleRate = intHeader(response, "X-InferHub-Audio-Sample-Rate");
  const characters = intHeader(response, "X-InferHub-Speech-Characters");
  for await (const { event, data } of readSseFrames(response.body)) {
    if (event === "speech.audio.error") {
      const error = (data.error ?? data) as JsonDict | string;
      const isObj = typeof error === "object" && error !== null;
      const message = isObj ? String((error as JsonDict).message ?? error) : String(error);
      const errorCode = isObj ? ((error as JsonDict).code as string | undefined) : undefined;
      throw new InferHubOpenAiException(response.status, message, JSON.stringify(data), {
        errorCode,
      });
    }
    const audioB64 = data.audio;
    const chunk: SpeechChunk = {
      type: (event ?? (data.type as string | undefined)) ?? "",
      audio: typeof audioB64 === "string" ? base64ToBytes(audioB64) : undefined,
      usage: data.usage as JsonDict | undefined,
      characters,
      servedBy,
      sampleRate,
      extra: Object.fromEntries(
        Object.entries(data).filter(([key]) => !["audio", "usage", "type"].includes(key)),
      ),
    };
    yield chunk;
    if (chunk.type === "speech.audio.done") return;
  }
}

// -- Images: synchronous OpenAI routes -----------------------------------------------------------

function imageGenerationRequestToJson(request: ImageGenerationRequest): JsonDict {
  const body: JsonDict = { model: request.model, prompt: request.prompt };
  if (request.negativePrompt !== undefined) body.negative_prompt = request.negativePrompt;
  if (request.n !== undefined) body.n = request.n;
  if (request.size !== undefined) body.size = request.size;
  if (request.seed !== undefined) body.seed = request.seed;
  if (request.responseFormat !== undefined) body.response_format = request.responseFormat;
  return body;
}

/** Numbers are formatted with plain `String()` — TypeScript has no locale-dependent decimal
 * separator surprise the way `CultureInfo` does in .NET (dotnet D4's problem does not exist here). */
function imageOptionsToHeaders(options?: ImageOptions): Record<string, string> {
  if (!options) return {};
  const headers: Record<string, string> = {};
  if (options.steps !== undefined) headers["X-InferHub-Image-Steps"] = String(options.steps);
  if (options.guidance !== undefined)
    headers["X-InferHub-Image-Guidance"] = String(options.guidance);
  if (options.seed !== undefined) headers["X-InferHub-Image-Seed"] = String(options.seed);
  if (options.strength !== undefined)
    headers["X-InferHub-Image-Strength"] = String(options.strength);
  if (options.maskConvention !== undefined)
    headers["X-InferHub-Image-Mask-Convention"] = options.maskConvention;
  if (options.seamRepair !== undefined)
    headers["X-InferHub-Image-Seam-Repair"] = options.seamRepair;
  if (options.projection !== undefined)
    headers["X-InferHub-Image-Projection"] = options.projection;
  return headers;
}

function imageEditForm(request: ImageEditRequest): FormData {
  const form = new FormData();
  form.append("model", request.model);
  form.append("prompt", request.prompt);
  form.append(
    "image",
    blobWithType(request.image, request.imageContentType),
    request.imageFilename,
  );
  if (request.mask !== undefined) {
    form.append(
      "mask",
      blobWithType(request.mask, request.maskContentType),
      request.maskFilename ?? "mask.png",
    );
  }
  return form;
}

function imageVariationForm(request: ImageVariationRequest): FormData {
  const form = new FormData();
  form.append("model", request.model);
  form.append(
    "image",
    blobWithType(request.image, request.imageContentType),
    request.imageFilename,
  );
  return form;
}

function imageDataFromJson(data: JsonDict): ImageData {
  const known = new Set([
    "b64_json",
    "size",
    "seed",
    "projection",
    "seam_delta",
    "seam_repair",
    "seam_delta_before",
    "revised_prompt",
  ]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    b64Json: data.b64_json as string | undefined,
    size: data.size as string | undefined,
    seed: data.seed as number | undefined,
    projection: data.projection as string | undefined,
    seamDelta: data.seam_delta as number | undefined,
    seamRepair: data.seam_repair as string | undefined,
    seamDeltaBefore: data.seam_delta_before as number | undefined,
    revisedPrompt: data.revised_prompt as string | undefined,
    extra,
  };
}

function imageResponseFromJson(data: JsonDict): ImageResponse {
  const known = new Set(["created", "data", "prompt_augmented", "trigger", "warnings"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    created: data.created as number | undefined,
    data: ((data.data as JsonDict[] | undefined) ?? []).map(imageDataFromJson),
    promptAugmented: data.prompt_augmented as string | undefined,
    trigger: data.trigger as string | undefined,
    warnings: data.warnings as string[] | undefined,
    extra,
  };
}

/** `POST /v1/images/generations`. */
export async function generateImage(
  request: Requester,
  req: ImageGenerationRequest,
): Promise<ImageResponse> {
  const response = await request("v1/images/generations", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...imageOptionsToHeaders(req.options) },
    body: JSON.stringify(imageGenerationRequestToJson(req)),
  });
  await raiseForStatus(response);
  return imageResponseFromJson((await response.json()) as JsonDict);
}

/** `POST /v1/images/edits`, multipart. */
export async function editImage(
  request: Requester,
  req: ImageEditRequest,
): Promise<ImageResponse> {
  const response = await request("v1/images/edits", {
    method: "POST",
    headers: imageOptionsToHeaders(req.options),
    body: imageEditForm(req),
  });
  await raiseForStatus(response);
  return imageResponseFromJson((await response.json()) as JsonDict);
}

/** `POST /v1/images/variations`, multipart. */
export async function createImageVariation(
  request: Requester,
  req: ImageVariationRequest,
): Promise<ImageResponse> {
  const response = await request("v1/images/variations", {
    method: "POST",
    headers: imageOptionsToHeaders(req.options),
    body: imageVariationForm(req),
  });
  await raiseForStatus(response);
  return imageResponseFromJson((await response.json()) as JsonDict);
}

// -- Images: the async job seam -------------------------------------------------------------------

function mediaJobOutputFromJson(data: JsonDict): MediaJobOutput {
  const known = new Set(["index", "url"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return { index: data.index as number | undefined, url: data.url as string | undefined, extra };
}

function mediaJobFromJson(data: JsonDict): MediaJob {
  const known = new Set(["id", "state", "capability", "step", "totalSteps", "images"]);
  const extra: JsonDict = {};
  for (const [key, value] of Object.entries(data)) if (!known.has(key)) extra[key] = value;
  return {
    id: (data.id as string) ?? "",
    state: (data.state as string) ?? "",
    capability: (data.capability as string) ?? "",
    step: data.step as number | undefined,
    totalSteps: data.totalSteps as number | undefined,
    images: ((data.images as JsonDict[] | undefined) ?? []).map(mediaJobOutputFromJson),
    extra,
  };
}

function mediaJobListFromJson(data: JsonDict): MediaJobList {
  return {
    jobs: ((data.jobs as JsonDict[] | undefined) ?? []).map(mediaJobFromJson),
    queued: (data.queued as number) ?? 0,
    active: (data.active as number) ?? 0,
    retainedBytes: (data.retainedBytes as number) ?? 0,
    retentionSeconds: (data.retentionSeconds as number) ?? 0,
    persistence: (data.persistence as string) ?? "",
  };
}

/** `POST /api/images/jobs`, JSON — queues a generation and returns immediately. */
export async function submitImageGeneration(
  request: Requester,
  req: ImageGenerationRequest,
): Promise<MediaJob> {
  const body = imageGenerationRequestToJson(req);
  body.operation = "generate";
  const response = await request("api/images/jobs", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...imageOptionsToHeaders(req.options) },
    body: JSON.stringify(body),
  });
  await raiseForStatus(response);
  return mediaJobFromJson((await response.json()) as JsonDict);
}

/** `POST /api/images/jobs`, multipart with `operation=edit`. */
export async function submitImageEdit(request: Requester, req: ImageEditRequest): Promise<MediaJob> {
  const form = imageEditForm(req);
  form.append("operation", "edit");
  const response = await request("api/images/jobs", {
    method: "POST",
    headers: imageOptionsToHeaders(req.options),
    body: form,
  });
  await raiseForStatus(response);
  return mediaJobFromJson((await response.json()) as JsonDict);
}

/** `POST /api/images/jobs`, multipart with `operation=variation`. */
export async function submitImageVariation(
  request: Requester,
  req: ImageVariationRequest,
): Promise<MediaJob> {
  const form = imageVariationForm(req);
  form.append("operation", "variation");
  const response = await request("api/images/jobs", {
    method: "POST",
    headers: imageOptionsToHeaders(req.options),
    body: form,
  });
  await raiseForStatus(response);
  return mediaJobFromJson((await response.json()) as JsonDict);
}

/** `GET /api/images/jobs` — this client's jobs, client-scoped. */
export async function listImageJobs(request: Requester): Promise<MediaJobList> {
  const response = await request("api/images/jobs", { method: "GET" });
  await raiseForStatus(response);
  return mediaJobListFromJson((await response.json()) as JsonDict);
}

/** `GET /api/images/jobs/{id}` — `undefined` on 404 (not yours, or not there — the same body
 * either way). */
export async function getImageJob(request: Requester, jobId: string): Promise<MediaJob | undefined> {
  const response = await request(`api/images/jobs/${jobId}`, { method: "GET" });
  if (response.status === 404) return undefined;
  await raiseForStatus(response);
  return mediaJobFromJson((await response.json()) as JsonDict);
}

/** `GET /api/images/jobs/{id}/events` — one `MediaJob` per SSE frame. Walking away does not
 * cancel the job. */
export async function* watchImageJob(request: Requester, jobId: string): AsyncGenerator<MediaJob> {
  const response = await request(`api/images/jobs/${jobId}/events`, { method: "GET" });
  await raiseForStatus(response);
  if (!response.body) {
    throw new InferHubError(response.status, "InferHub streaming response had no body.");
  }
  for await (const { data } of readSseFrames(response.body)) {
    const job = mediaJobFromJson(data);
    yield job;
    if (["succeeded", "failed", "cancelled", "expired"].includes(job.state)) return;
  }
}

/** `GET /api/images/jobs/{id}/content/{index}` — read once. The caller consumes
 * `result.response.body`/`.arrayBuffer()`. `410 job_expired` on a repeat read, `409
 * job_not_ready`, `404 image_not_found`. */
export async function openImageContent(
  request: Requester,
  jobId: string,
  index: number,
): Promise<ImageContent> {
  const response = await request(`api/images/jobs/${jobId}/content/${index}`, { method: "GET" });
  if (!response.ok) {
    await raiseForStatus(response);
  }
  return {
    response,
    contentType: response.headers.get("Content-Type") ?? undefined,
    projection: response.headers.get("X-InferHub-Image-Projection") ?? undefined,
    seamRepair: response.headers.get("X-InferHub-Image-Seam-Repair") ?? undefined,
  };
}

/** `DELETE /api/images/jobs/{id}` — best effort; the returned job says what actually happened. A
 * job already terminal is a `409 job_terminal`. */
export async function cancelImageJob(request: Requester, jobId: string): Promise<MediaJob> {
  const response = await request(`api/images/jobs/${jobId}`, { method: "DELETE" });
  await raiseForStatus(response);
  return mediaJobFromJson((await response.json()) as JsonDict);
}
