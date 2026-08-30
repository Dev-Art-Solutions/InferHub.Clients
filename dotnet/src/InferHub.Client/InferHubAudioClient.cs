using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using InferHub.Client.Configuration;
using InferHub.Client.Exceptions;
using InferHub.Client.Http;
using InferHub.Client.Models.Audio;
using InferHub.Client.Serialization;

namespace InferHub.Client;

/// <inheritdoc cref="IInferHubAudioClient"/>
public sealed class InferHubAudioClient : IInferHubAudioClient
{
    private static InferHubJsonContext Json => InferHubJsonContext.Default;

    private readonly HttpClient httpClient;
    private readonly TimeSpan requestTimeout;

    /// <summary>
    /// Create a new client. Prefer <c>services.AddInferHubClient(...)</c> in DI, which registers
    /// this client with an infinite <see cref="HttpClient.Timeout"/> — a streamed synthesis
    /// outlives the 100-second default, and an <see cref="HttpClient"/> timeout would abort it
    /// mid-sentence — and applies <see cref="InferHubClientOptions.Timeout"/> per transcription
    /// instead.
    /// </summary>
    /// <param name="httpClient">Transport. Set <c>Timeout = Timeout.InfiniteTimeSpan</c> when constructing this by hand.</param>
    /// <param name="options">Client options; <c>null</c> means no per-call timeout.</param>
    public InferHubAudioClient(HttpClient httpClient, InferHubClientOptions? options = null)
    {
        this.httpClient = httpClient;
        requestTimeout = options?.Timeout ?? Timeout.InfiniteTimeSpan;
    }

    /// <inheritdoc/>
    public async Task<Transcription> TranscribeAsync(TranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            using var content = BuildForm(request, TranscriptionFormats.VerboseJson);
            using var response = await PostAsync(content, token);

            return await response.Content.ReadFromJsonAsync(Json.Transcription, token)
                is { } transcription
                ? Apply(transcription, response)
                : throw new InferHubException(response.StatusCode, "empty response body", string.Empty);
        });

        static Transcription Apply(Transcription transcription, HttpResponseMessage response)
        {
            transcription.ServedBy = InferHubHeaders.ReadServedBy(response);
            return transcription;
        }
    }

    /// <inheritdoc/>
    public async Task<TranscriptionDocument> TranscribeDocumentAsync(TranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = StartRequestTimeout(cancellationToken, out var token);

        return await WithTimeoutAsync(cancellationToken, async () =>
        {
            var format = string.IsNullOrWhiteSpace(request.ResponseFormat)
                ? TranscriptionFormats.Json
                : request.ResponseFormat;

            using var content = BuildForm(request, format);
            using var response = await PostAsync(content, token);

            // The hub's own bytes, decoded and otherwise untouched: an srt file is a file.
            var body = await response.Content.ReadAsStringAsync(token);

            return new TranscriptionDocument(
                format,
                response.Content.Headers.ContentType?.ToString() ?? "application/json",
                body)
            {
                ServedBy = InferHubHeaders.ReadServedBy(response)
            };
        });
    }

    /// <inheritdoc/>
    public async Task<SpeechAudio> CreateSpeechAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // No per-call timeout: the caller reads the body after this method returns, and a timer
        // still running then would abort a synthesis mid-sentence.
        var response = await SendSpeechAsync(request, cancellationToken);

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            return new SpeechAudio(
                response,
                stream,
                response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
                response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'),
                InferHubHeaders.ReadServedBy(response),
                (int?)InferHubHeaders.ReadInt64(response, InferHubHeaders.AudioSampleRate),
                InferHubHeaders.ReadInt64(response, InferHubHeaders.SpeechCharacters));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SpeechChunk> StreamSpeechAsync(
        SpeechRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.StreamFormat = SpeechStreamFormats.Sse;

        using var response = await SendSpeechAsync(request, cancellationToken);

        var servedBy = InferHubHeaders.ReadServedBy(response);
        var sampleRate = (int?)InferHubHeaders.ReadInt64(response, InferHubHeaders.AudioSampleRate);
        var characters = InferHubHeaders.ReadInt64(response, InferHubHeaders.SpeechCharacters);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var frame in SseFrameReader.ReadAsync(stream, cancellationToken))
        {
            // The frame's own `type` decides what it is; the hub writes the same value on the
            // `event:` line, so keying on the payload costs one field of an object already parsed.
            var type = ReadFrameType(frame.Data) ?? frame.Event;

            if (type == SpeechEvents.Error)
            {
                throw ErrorFrame(frame.Data, response);
            }

            SpeechChunk chunk;

            try
            {
                chunk = JsonSerializer.Deserialize(frame.Data, Json.SpeechChunk)!;
            }
            catch (JsonException ex)
            {
                throw new InferHubException(response.StatusCode, $"Malformed SSE frame: {ex.Message}", frame.Data);
            }

            if (chunk is null)
            {
                continue;
            }

            chunk.Type ??= type;
            chunk.ServedBy = servedBy;
            chunk.SampleRate = sampleRate;
            chunk.Characters = characters;

            yield return chunk;

            if (type == SpeechEvents.Done)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// The multipart body, with <b>every field written before the file part</b>.
    /// </summary>
    /// <remarks>
    /// Above the hub's <c>Tools:MaxStreamedBytes</c> the request is routed from the leading fields
    /// while the bytes are still arriving, so a field after the file is refused with a <c>400</c>
    /// rather than dropped — and a <c>language</c> the hub never saw would be a transcription
    /// answered in the wrong language with nothing in the response to explain it. The small-file
    /// path tolerates any order, which is why getting this wrong shows up on the first large file
    /// and not before.
    /// </remarks>
    private static MultipartFormDataContent BuildForm(TranscriptionRequest request, string responseFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);

        if (request.Audio is null)
        {
            throw new ArgumentException($"{nameof(TranscriptionRequest.Audio)} is required.", nameof(request));
        }

        var content = new MultipartFormDataContent();

        try
        {
            content.Add(new StringContent(request.Model), "model");
            content.Add(new StringContent(responseFormat), "response_format");

            if (!string.IsNullOrWhiteSpace(request.Language))
            {
                content.Add(new StringContent(request.Language), "language");
            }

            if (!string.IsNullOrWhiteSpace(request.Prompt))
            {
                content.Add(new StringContent(request.Prompt), "prompt");
            }

            if (request.Temperature is double temperature)
            {
                content.Add(
                    new StringContent(temperature.ToString(CultureInfo.InvariantCulture)),
                    "temperature");
            }

            // Last, always.
            var file = new StreamContent(request.Audio);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(
                string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType);

            // The part name is `file` because the hub looks for it by name; the most common
            // mistake against this API is calling it `audio`.
            content.Add(file, "file", string.IsNullOrWhiteSpace(request.FileName) ? "audio" : request.FileName);

            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> PostAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync("v1/audio/transcriptions", content, cancellationToken);

        try
        {
            await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendSpeechAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/audio/speech")
        {
            Content = JsonContent.Create(request, Json.SpeechRequest)
        };

        var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        try
        {
            await InferHubResponse.EnsureSuccessAsync(response, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>The frame's <c>type</c>, or null when the payload is not an object that has one.</summary>
    private static string? ReadFrameType(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                    ? type.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A <c>speech.audio.error</c> frame, turned into the same exception the same failure would
    /// have raised had it arrived before the <c>200</c>. The envelope inside the frame is the
    /// OpenAI one, so it is read by the same parser — including a <c>code</c> that may be a number.
    /// </summary>
    private static Exception ErrorFrame(string payload, HttpResponseMessage response)
    {
        var error = InferHubResponse.TryReadOpenAiError(payload);

        return new InferHubOpenAiException(
            response.StatusCode,
            error?.Message ?? "the synthesis failed after it had started",
            payload,
            error?.Type,
            error?.Code,
            error?.Param);
    }

    /// <summary>
    /// Translate a per-call timeout expiry into <see cref="TimeoutException"/>, so a caller can tell
    /// "I cancelled" from "the hub took too long".
    /// </summary>
    private async Task<T> WithTimeoutAsync<T>(CancellationToken callerToken, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && requestTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new TimeoutException($"The InferHub transcription timed out after {requestTimeout.TotalSeconds:0.#}s.");
        }
    }

    private CancellationTokenSource? StartRequestTimeout(CancellationToken cancellationToken, out CancellationToken token)
    {
        if (requestTimeout == Timeout.InfiniteTimeSpan)
        {
            token = cancellationToken;
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(requestTimeout);
        token = cts.Token;
        return cts;
    }

    /// <summary>The three event names a streamed synthesis uses. <c>error</c> is InferHub's own.</summary>
    private static class SpeechEvents
    {
        public const string Delta = "speech.audio.delta";
        public const string Done = "speech.audio.done";
        public const string Error = "speech.audio.error";
    }
}
