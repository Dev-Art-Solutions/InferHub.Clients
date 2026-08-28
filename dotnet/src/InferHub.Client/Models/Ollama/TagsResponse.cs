using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>Response for <c>GET /api/tags</c>.</summary>
public sealed record TagsResponse(
    [property: JsonPropertyName("models")] IReadOnlyList<ModelInfo> Models);
