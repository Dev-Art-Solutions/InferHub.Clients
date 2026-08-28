using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Vector;

/// <summary>
/// One ranked hit from <c>POST /api/vector/{collection}/query</c> or
/// <c>.../retrieve</c>. Higher <see cref="Score"/> is a closer match.
/// </summary>
public sealed class VectorMatch
{
    /// <summary>Id of the matched record.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Similarity score for the collection's configured distance metric.</summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>
    /// The matched record's payload. Use
    /// <see cref="VectorPayloadExtensions.As{T}(JsonElement?, JsonSerializerOptions?)"/> to deserialize it.
    /// </summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    /// <summary>The matched record's metadata.</summary>
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }

    /// <summary>Any additional fields the coordinator returns.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
