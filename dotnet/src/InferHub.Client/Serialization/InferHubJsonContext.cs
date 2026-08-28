using System.Text.Json;
using System.Text.Json.Serialization;
using InferHub.Client.Models;
using InferHub.Client.Models.Admin;
using InferHub.Client.Models.Ollama;
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
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class InferHubJsonContext : JsonSerializerContext
{
}
