using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Vector;

/// <summary>
/// A stored vector record — the shape returned by <c>POST /api/vector/{collection}/upsert</c>
/// and <c>GET /api/vector/{collection}/{id}</c>.
/// </summary>
public sealed class VectorRecord
{
    /// <summary>Caller-assigned record id, unique within the collection.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The stored vector — either supplied directly or produced by embedding <c>text</c>.</summary>
    [JsonPropertyName("vector")]
    public float[] Vector { get; set; } = Array.Empty<float>();

    /// <summary>
    /// Opaque caller payload, echoed back verbatim. Use
    /// <see cref="VectorPayloadExtensions.As{T}(JsonElement?, JsonSerializerOptions?)"/>
    /// to deserialize it into your own type.
    /// </summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }

    /// <summary>Flat string→string metadata used for query filtering.</summary>
    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }

    /// <summary>Monotonic sequence number assigned by the store on write.</summary>
    [JsonPropertyName("seqNo")]
    public long SeqNo { get; set; }

    /// <summary>Server timestamp of the write, in UTC.</summary>
    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>Any additional fields the coordinator returns.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
