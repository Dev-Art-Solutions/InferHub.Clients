using System.Net;
using System.Text;
using System.Text.Json;
using InferHub.Client.Exceptions;
using InferHub.Client.Models.Audio;

namespace InferHub.Client.Tests;

/// <summary>
/// The audio surface (<c>/v1/audio/*</c>), phase 9.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal in this file was recorded from a live hub</b> — InferHub 3.37.0, one node
/// serving <c>chat</c> and <c>embed</c> with <c>tools.enabled=false</c> — by driving the two routes
/// with curl and pasting what came back, escapes and all.
/// </para>
/// <para>
/// <b>The three success shapes are derived, not recorded, and that is the honest state:</b> the hub
/// available on the day had no node providing <c>transcribe</c> or <c>speak</c>, so no successful
/// transcription and no synthesised audio was ever observed. They are taken from the hub's own
/// serializers — <c>SpeechStream.Delta/Done</c>, <c>VerboseTranscription</c>,
/// <c>TranscriptFormatter.ToSrt</c> — which decide those bytes, rather than from what a client
/// author expects them to look like. Each is marked below. Phase 25 is where a real one arrives.
/// </para>
/// </remarks>
public class InferHubAudioClientTests
{
    // ---- recorded from the live hub -------------------------------------------------------

    private const string RecordedNoSpeakNode = """
        {"error":{"message":"no node currently provides 'speak' for model 'gemma:2b'","type":"api_error","param":null,"code":"capability_unavailable"}}
        """;

    private const string RecordedNoTranscribeNode = """
        {"error":{"message":"no node currently provides 'transcribe' for model 'gemma:2b'","type":"api_error","param":null,"code":"capability_unavailable"}}
        """;

    private const string RecordedModelNotFound = """
        {"error":{"message":"model 'whisper-1' not found","type":"not_found_error","param":"model","code":"model_not_found"}}
        """;

    // A transcription refusal names `model`, a speech one names `input` — the field the hub blames
    // is not the field at fault, and a client that mapped `param` onto its own property names would
    // point a caller at the wrong one.
    private const string RecordedBadTranscriptionFormat = """
        {"error":{"message":"response_format 'docx' is not supported. Use one of: json, text, srt, vtt, verbose_json.","type":"invalid_request_error","param":"model","code":null}}
        """;

    private const string RecordedBadSpeechFormat = """
        {"error":{"message":"response_format 'aiff' is not supported. Use one of: wav, mp3, opus, flac, pcm.","type":"invalid_request_error","param":"input","code":null}}
        """;

    private const string RecordedUnstreamableFormat = """
        {"error":{"message":"response_format 'mp3' cannot be streamed. Use one of: wav, pcm, or drop stream_format to get the whole file at once.","type":"invalid_request_error","param":"input","code":null}}
        """;

    // The four the client's own guards make unreachable from C# — it always sends `model` and a
    // `file` part, and always as multipart. Kept because they are recorded bodies a phase-15 case
    // can be built from, and because a client that hid the sentence would be the failure the hub
    // wrote it to prevent.
    private const string RecordedNotMultipart = """
        {"error":{"message":"this endpoint takes multipart/form-data with a 'file' part","type":"invalid_request_error","param":null,"code":null}}
        """;

    private const string RecordedNoFilePart = """
        {"error":{"message":"a 'file' part is required","type":"invalid_request_error","param":"model","code":null}}
        """;

    private const string RecordedNoModel = """
        {"error":{"message":"model is required","type":"invalid_request_error","param":"model","code":null}}
        """;

    private const string RecordedEmptyInput = """
        {"error":{"message":"input is required","type":"invalid_request_error","param":"input","code":null}}
        """;

    private const string RecordedUnknownStreamFormat = """
        {"error":{"message":"stream_format 'ndjson' is not supported. Use one of: sse, audio.","type":"invalid_request_error","param":"input","code":null}}
        """;

    // ---- derived from the hub's own serializers, never observed ----------------------------

    // VerboseTranscription: task, language, duration, text, segments — its own JsonPropertyName
    // order, serialized with JsonSerializerDefaults.Web.
    private const string DerivedVerboseTranscription = """
        {"task":"transcribe","language":"en","duration":3.52,"text":"Hello from the fleet.","segments":[{"id":0,"start":0,"end":1.44,"text":"Hello"},{"id":1,"start":1.44,"end":3.52,"text":" from the fleet."}]}
        """;

    // TranscriptFormatter.ToSrt: a 1-based counter, HH:MM:SS,mmm with a COMMA (WebVTT uses a
    // period), the line, a blank line.
    private const string DerivedSrt =
        "1\n00:00:00,000 --> 00:00:01,440\nHello\n\n2\n00:00:01,440 --> 00:00:03,520\nfrom the fleet.\n\n";

    // SpeechStream.Frame(name, json): the event name on its own line, then the JSON, then a blank
    // line. `type` inside the payload repeats the event name — which is why this client keys on it.
    // The terminal frame's three zeros are a true count, not a placeholder.
    private static readonly string DerivedSpeechStream =
        Frame("speech.audio.delta", $$"""{"type":"speech.audio.delta","audio":"{{B64("RIFFhead")}}"}""")
        + Frame("speech.audio.delta", $$"""{"type":"speech.audio.delta","audio":"{{B64("samples1")}}"}""")
        + Frame("speech.audio.done", """{"type":"speech.audio.done","usage":{"input_tokens":0,"output_tokens":0,"total_tokens":0}}""");

    private const string DerivedSpeechErrorFrame =
        "event: speech.audio.delta\ndata: {\"type\":\"speech.audio.delta\",\"audio\":\"UklGRmhlYWQ=\"}\n\n"
        + "event: speech.audio.error\ndata: {\"type\":\"speech.audio.error\",\"error\":{\"message\":\"the node handling this request disconnected before it answered\",\"type\":\"api_error\",\"param\":null,\"code\":\"node_lost\"}}\n\n";

    private static string Frame(string name, string json) => $"event: {name}\ndata: {json}\n\n";

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static (InferHubAudioClient Client, FakeHttpMessageHandler Handler) CreateClient(
        HttpStatusCode status,
        string body,
        string mediaType = "application/json")
    {
        var handler = new FakeHttpMessageHandler(status, body, mediaType);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5080/") };
        return (new InferHubAudioClient(http), handler);
    }

    private static TranscriptionRequest Audio(string model = "whisper-1")
        => TranscriptionRequest.FromBytes(model, Encoding.UTF8.GetBytes("RIFFfake-wav"), "meeting.wav", "audio/wav");

    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_posts_the_v1_path_and_forces_verbose_json()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedVerboseTranscription);

        var request = Audio();
        request.ResponseFormat = TranscriptionFormats.Srt;   // ignored: this method parses one shape

        await client.TranscribeAsync(request);

        Assert.EndsWith("v1/audio/transcriptions", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("name=response_format", handler.RequestBodies[0]);
        Assert.Contains("verbose_json", handler.RequestBodies[0]);
        Assert.DoesNotContain("srt", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task TranscribeAsync_parses_text_language_duration_and_segments()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedVerboseTranscription);
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node-1";

        var transcript = await client.TranscribeAsync(Audio());

        Assert.Equal("Hello from the fleet.", transcript.Text);
        Assert.Equal("en", transcript.Language);
        Assert.Equal(3.52, transcript.Duration);
        Assert.Equal(2, transcript.Segments.Count);
        Assert.Equal(1.44, transcript.Segments[1].Start);
        Assert.Equal(" from the fleet.", transcript.Segments[1].Text);
        Assert.Equal("node-1", transcript.ServedBy);
    }

    /// <summary>
    /// The one a caller cannot discover on a small file: above the hub's <c>Tools:MaxStreamedBytes</c>
    /// the request is routed from the leading fields while the bytes are still arriving, so a field
    /// after the file is a 400 — and the small-file path tolerates any order, which is why getting
    /// this wrong first shows up in production.
    /// </summary>
    [Fact]
    public async Task Every_form_field_is_written_before_the_file_part()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedVerboseTranscription);

        var request = Audio();
        request.Language = "bg";
        request.Prompt = "InferHub, Qdrant";
        request.Temperature = 0.2;

        await client.TranscribeAsync(request);

        var body = handler.RequestBodies[0];
        var file = body.IndexOf("name=file", StringComparison.Ordinal);

        Assert.True(file > 0, "the file part is missing");
        foreach (var field in new[] { "name=model", "name=response_format", "name=language", "name=prompt", "name=temperature" })
        {
            Assert.InRange(body.IndexOf(field, StringComparison.Ordinal), 0, file);
        }

        // The part is named `file`, and the filename rides on it — the commonest mistake against
        // this API is calling the part `audio`.
        Assert.Contains("filename=meeting.wav", body);
    }

    [Fact]
    public async Task Temperature_is_written_with_an_invariant_decimal_point()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedVerboseTranscription);

        var request = Audio();
        request.Temperature = 0.2;

        await client.TranscribeAsync(request);

        // A Bulgarian or German host formats this as "0,2" by default, which the hub rejects as
        // "not a number" — on exactly the machines nobody runs CI on.
        Assert.Contains("0.2", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task TranscribeDocumentAsync_returns_the_hubs_srt_verbatim()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSrt, "text/plain");

        var request = Audio();
        request.ResponseFormat = TranscriptionFormats.Srt;

        var document = await client.TranscribeDocumentAsync(request);

        Assert.Equal(TranscriptionFormats.Srt, document.Format);
        Assert.Equal(DerivedSrt, document.Content);
        Assert.StartsWith("text/plain", document.ContentType);
        Assert.Contains("00:00:01,440", document.Content);   // comma, not a period: SubRip, not VTT
        Assert.Contains("name=response_format", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task TranscribeAsync_without_audio_throws_before_a_request_goes_out()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedVerboseTranscription);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.TranscribeAsync(new TranscriptionRequest { Model = "whisper-1" }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_fleet_with_no_transcribe_node_is_a_503_naming_the_capability()
    {
        var (client, _) = CreateClient(HttpStatusCode.ServiceUnavailable, RecordedNoTranscribeNode);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.TranscribeAsync(Audio("gemma:2b")));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal("capability_unavailable", error.ErrorCode);
        Assert.Equal("api_error", error.ErrorType);
        Assert.Contains("transcribe", error.Message);
    }

    [Fact]
    public async Task A_model_no_node_holds_is_a_404_and_not_a_503()
    {
        var (client, _) = CreateClient(HttpStatusCode.NotFound, RecordedModelNotFound);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.TranscribeAsync(Audio()));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("model_not_found", error.ErrorCode);
        Assert.Equal("model", error.Param);
    }

    [Fact]
    public async Task A_refused_transcription_format_names_model_and_a_refused_speech_format_names_input()
    {
        var (transcribe, _) = CreateClient(HttpStatusCode.BadRequest, RecordedBadTranscriptionFormat);
        var request = Audio();
        request.ResponseFormat = "docx";

        var one = await Assert.ThrowsAsync<InferHubOpenAiException>(() => transcribe.TranscribeDocumentAsync(request));

        var (speech, _) = CreateClient(HttpStatusCode.BadRequest, RecordedBadSpeechFormat);
        var two = await Assert.ThrowsAsync<InferHubOpenAiException>(
            () => speech.CreateSpeechAsync(new SpeechRequest { Model = "piper", Input = "hi", ResponseFormat = "aiff" }));

        // Both are invalid_request_error with a null code; the field they blame differs, and the
        // client reports what arrived rather than tidying it up.
        Assert.Equal("model", one.Param);
        Assert.Equal("input", two.Param);
        Assert.Null(one.ErrorCode);
        Assert.Null(two.ErrorCode);
        Assert.Contains("json, text, srt, vtt, verbose_json", one.Message);
        Assert.Contains("wav, mp3, opus, flac, pcm", two.Message);
    }

    [Theory]
    [InlineData(RecordedNotMultipart, "multipart/form-data")]
    [InlineData(RecordedNoFilePart, "'file' part is required")]
    [InlineData(RecordedNoModel, "model is required")]
    [InlineData(RecordedEmptyInput, "input is required")]
    [InlineData(RecordedUnknownStreamFormat, "sse, audio")]
    public async Task The_hubs_own_sentence_reaches_the_caller_unflattened(string body, string expected)
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, body);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(() => client.TranscribeAsync(Audio()));

        // The message, not the JSON it arrived in: an SDK reads error.message to build what it
        // raises, and a client that surfaces the whole envelope hands the caller a wall of braces.
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("{", error.Message);
        Assert.Equal("invalid_request_error", error.ErrorType);
    }

    [Fact]
    public async Task Asking_to_stream_a_format_that_cannot_be_streamed_is_refused_by_the_hub()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, RecordedUnstreamableFormat);

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(async () =>
        {
            await foreach (var _ in client.StreamSpeechAsync(new SpeechRequest
            {
                Model = "piper",
                Input = "hi",
                ResponseFormat = SpeechFormats.Mp3
            }))
            {
            }
        });

        // Refused before a node is chosen, so nothing was spent or synthesised.
        Assert.Contains("cannot be streamed", error.Message);
        Assert.Contains("wav, pcm", error.Message);
    }

    [Fact]
    public async Task CreateSpeechAsync_hands_back_the_stream_the_content_type_and_the_file_name()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, "RIFFfake-wav-bytes", "audio/wav");
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node-1";
        handler.ContentHeaders["Content-Disposition"] = "attachment; filename=speech.wav";

        await using var speech = await client.CreateSpeechAsync(SpeechRequest.Create("piper", "Hello."));

        // Verbatim, parameters and all — the `; charset=utf-8` here is the test double's
        // StringContent, and a client that stripped parameters would be editing the hub's answer.
        Assert.StartsWith("audio/wav", speech.ContentType);
        Assert.Equal("speech.wav", speech.FileName);
        Assert.Equal("node-1", speech.ServedBy);

        // The buffered answer carries neither audio header: the hub sends them on streamed
        // responses only, and a zero invented here would be a measurement nobody took.
        Assert.Null(speech.SampleRate);
        Assert.Null(speech.Characters);

        Assert.Equal("RIFFfake-wav-bytes", Encoding.UTF8.GetString(await speech.ReadAllBytesAsync()));
        Assert.EndsWith("v1/audio/speech", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateSpeechAsync_reads_the_same_way_when_the_hub_streams_the_raw_container()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, "RIFFchunk1chunk2", "audio/wav");
        handler.ResponseHeaders["X-InferHub-Audio-Sample-Rate"] = "22050";
        handler.ResponseHeaders["X-InferHub-Speech-Characters"] = "38";

        await using var speech = await client.CreateSpeechAsync(new SpeechRequest
        {
            Model = "piper",
            Input = "Hello from the fleet, at some length.",
            StreamFormat = SpeechStreamFormats.Audio
        });

        // Not one byte of caller code differs from the buffered call above — that is the point.
        Assert.Equal(22050, speech.SampleRate);
        Assert.Equal(38, speech.Characters);
        Assert.Null(speech.FileName);   // a streamed answer has no Content-Disposition

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("audio", sent.GetProperty("stream_format").GetString());
    }

    [Fact]
    public async Task StreamSpeechAsync_forces_sse_and_omits_what_the_caller_did_not_set()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSpeechStream, "text/event-stream");

        await foreach (var _ in client.StreamSpeechAsync(SpeechRequest.Create("piper", "Hello.")))
        {
        }

        var sent = JsonDocument.Parse(handler.RequestBodies[0]).RootElement;
        Assert.Equal("sse", sent.GetProperty("stream_format").GetString());
        Assert.Equal("piper", sent.GetProperty("model").GetString());
        Assert.False(sent.TryGetProperty("voice", out _));
        Assert.False(sent.TryGetProperty("speed", out _));
    }

    [Fact]
    public async Task StreamSpeechAsync_yields_the_audio_then_the_terminal_frame_with_a_true_zero()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, DerivedSpeechStream, "text/event-stream");
        handler.ResponseHeaders["X-InferHub-Served-By"] = "node-1";
        handler.ResponseHeaders["X-InferHub-Audio-Sample-Rate"] = "22050";
        handler.ResponseHeaders["X-InferHub-Speech-Characters"] = "6";

        var chunks = new List<SpeechChunk>();
        await foreach (var chunk in client.StreamSpeechAsync(SpeechRequest.Create("piper", "Hello.")))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.Equal("RIFFhead", Encoding.UTF8.GetString(chunks[0].Audio));
        Assert.Equal("samples1", Encoding.UTF8.GetString(chunks[1].Audio));

        var terminal = chunks[^1];
        Assert.Equal("speech.audio.done", terminal.Type);
        Assert.Empty(terminal.Audio);

        // Three zeros, and they are measured: a phoneme model tokenized nothing. The number that
        // reconciles with a bill is the character count on the header.
        Assert.NotNull(terminal.Usage);
        Assert.Equal(0, terminal.Usage!.TotalTokens);
        Assert.Equal(6, terminal.Characters);

        // Read once, stamped on every chunk — including the sample rate, which for `pcm` is the
        // only place it exists.
        Assert.All(chunks, c => Assert.Equal("node-1", c.ServedBy));
        Assert.All(chunks, c => Assert.Equal(22050, c.SampleRate));
    }

    [Fact]
    public async Task A_speech_audio_error_frame_becomes_an_exception_and_the_partial_answer_is_kept()
    {
        var (client, _) = CreateClient(HttpStatusCode.OK, DerivedSpeechErrorFrame, "text/event-stream");

        var heard = new List<byte[]>();

        var error = await Assert.ThrowsAsync<InferHubOpenAiException>(async () =>
        {
            await foreach (var chunk in client.StreamSpeechAsync(SpeechRequest.Create("piper", "Hello.")))
            {
                heard.Add(chunk.Audio);
            }
        });

        // The hub's own extension: past the first byte there is no status left to send, so the
        // ending is a frame. A half-written answer plus a clean exception is the contract.
        Assert.Single(heard);
        Assert.Equal("node_lost", error.ErrorCode);
        Assert.Contains("disconnected", error.Message);
    }

    [Fact]
    public async Task The_stream_stops_at_the_terminal_frame_and_ignores_anything_after_it()
    {
        var (client, _) = CreateClient(
            HttpStatusCode.OK,
            DerivedSpeechStream + Frame("speech.audio.delta", """{"type":"speech.audio.delta","audio":"YWZ0ZXI="}"""),
            "text/event-stream");

        var chunks = new List<SpeechChunk>();
        await foreach (var chunk in client.StreamSpeechAsync(SpeechRequest.Create("piper", "Hello.")))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
    }
}
