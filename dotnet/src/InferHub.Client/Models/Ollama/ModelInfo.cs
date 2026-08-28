using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Ollama;

/// <summary>A single entry in <c>GET /api/tags</c> — one model advertised by the mesh.</summary>
public sealed record ModelInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("digest")] string? Digest,
    [property: JsonPropertyName("size")] long? SizeBytes);
