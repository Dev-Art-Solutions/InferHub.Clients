using System.Text.Json;
using System.Text.Json.Serialization;
using InferHub.Client.Models;
using InferHub.Client.Models.Admin;
using InferHub.Client.Models.Audio;
using InferHub.Client.Models.Corpus;
using InferHub.Client.Models.Images;
using InferHub.Client.Models.Ollama;
using InferHub.Client.Models.OpenAi;
using InferHub.Client.Models.Videos;
using InferHub.Client.Models.Vector;

namespace InferHub.Client.Serialization;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for every DTO the client sends or
/// receives. Wiring the typed surface through this context (instead of reflection-based
/// serialization) keeps the library trim- and AOT-friendly: the metadata is generated at
/// compile time, so no runtime reflection over the DTO graph is needed.
/// </summary>
/// <remarks>
/// The generic payload escape hatches (<see cref="VectorPayloadExtensions.As{T}(System.Text.Json.JsonElement?, System.Text.Json.JsonSerializerOptions?)"/>,
/// <see cref="VectorUpsert.WithPayload{T}(T, System.Text.Json.JsonSerializerOptions?)"/>) still use
/// reflection for the caller's own type and are annotated accordingly; pass a
/// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/> overload to stay AOT-safe.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TagsResponse))]
[JsonSerializable(typeof(ModelInfo))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(GenerateRequest))]
[JsonSerializable(typeof(GenerateResponse))]
[JsonSerializable(typeof(EmbedRequest))]
[JsonSerializable(typeof(EmbedResponse))]
[JsonSerializable(typeof(EmbeddingsRequest))]
[JsonSerializable(typeof(EmbeddingsResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(VectorUpsert))]
[JsonSerializable(typeof(VectorRecord))]
[JsonSerializable(typeof(VectorQuery))]
[JsonSerializable(typeof(VectorMatch))]
[JsonSerializable(typeof(VectorMatchesResponse))]
[JsonSerializable(typeof(AdminNode))]
[JsonSerializable(typeof(AdminNode[]))]
[JsonSerializable(typeof(CollectionsResponse))]
[JsonSerializable(typeof(CollectionDetail))]
[JsonSerializable(typeof(CollectionInfo))]
[JsonSerializable(typeof(CreateCollectionRequest))]
[JsonSerializable(typeof(AdminSnapshotPayload))]
[JsonSerializable(typeof(AdminVectorEventPayload))]
[JsonSerializable(typeof(NodeProfile))]
[JsonSerializable(typeof(NodeProfile[]))]
[JsonSerializable(typeof(NodeProfileState))]
[JsonSerializable(typeof(PutProfileResult))]
[JsonSerializable(typeof(DeleteProfileResult))]
[JsonSerializable(typeof(ModelCommandAccepted))]
[JsonSerializable(typeof(FleetModelMatrix))]
[JsonSerializable(typeof(EnsureModelResult))]
[JsonSerializable(typeof(UsageResponse))]
[JsonSerializable(typeof(ClientRow[]))]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(ChatCompletionChunk))]
[JsonSerializable(typeof(CompletionRequest))]
[JsonSerializable(typeof(CompletionResponse))]
[JsonSerializable(typeof(OpenAiEmbeddingsRequest))]
[JsonSerializable(typeof(OpenAiEmbeddingsResponse))]
[JsonSerializable(typeof(OpenAiModel))]
[JsonSerializable(typeof(OpenAiModelList))]
[JsonSerializable(typeof(Transcription))]
[JsonSerializable(typeof(TranscriptionSegment))]
[JsonSerializable(typeof(SpeechRequest))]
[JsonSerializable(typeof(SpeechChunk))]
[JsonSerializable(typeof(SpeechUsage))]
[JsonSerializable(typeof(ImageGenerationRequest))]
[JsonSerializable(typeof(ImageResponse))]
[JsonSerializable(typeof(ImageData))]
[JsonSerializable(typeof(MediaJob))]
[JsonSerializable(typeof(MediaJobOutput))]
[JsonSerializable(typeof(MediaJobList))]
[JsonSerializable(typeof(VideoGenerationRequest))]
[JsonSerializable(typeof(Video))]
[JsonSerializable(typeof(VideoError))]
[JsonSerializable(typeof(VideoDeletion))]
[JsonSerializable(typeof(TextDocument))]
[JsonSerializable(typeof(IngestResult))]
[JsonSerializable(typeof(DocumentSummary))]
[JsonSerializable(typeof(DocumentsResponse))]
[JsonSerializable(typeof(DocumentChunk))]
[JsonSerializable(typeof(ChunksResponse))]
[JsonSerializable(typeof(DocumentDeletion))]
[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(SearchHit))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class InferHubJsonContext : JsonSerializerContext
{
}
