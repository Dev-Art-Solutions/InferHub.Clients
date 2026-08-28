using System.Text.Json;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Vector;

/// <summary>
/// Body for <c>POST /api/vector/{collection}/query</c> and <c>.../retrieve</c>. Search by a
/// raw <see cref="Vector"/> or by <see cref="Text"/> (embedded on a node using <see cref="Model"/>).
/// Use <see cref="FromVector(float[], int)"/> or <see cref="FromText(string, string?, int)"/>.
/// </summary>
public sealed class VectorQuery
{
    /// <summary>Query vector. Leave null to have the coordinator embed <see cref="Text"/>.</summary>
    [JsonPropertyName("vector")]
    public float[]? Vector { get; set; }

    /// <summary>Maximum number of matches to return. Defaults to 10 (the coordinator's default).</summary>
    [JsonPropertyName("k")]
    public int K { get; set; } = 10;

    /// <summary>Exact-match metadata filter applied before ranking.</summary>
    [JsonPropertyName("filter")]
    public IReadOnlyDictionary<string, string>? Filter { get; set; }

    /// <summary>Text to embed and search with when <see cref="Vector"/> is not supplied.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Embedding model tag used when embedding <see cref="Text"/>.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>Any additional fields to pass through.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    /// <summary>Build a query from a pre-computed vector.</summary>
    public static VectorQuery FromVector(float[] vector, int k = 10)
    {
        ArgumentNullException.ThrowIfNull(vector);
        return new VectorQuery { Vector = vector, K = k };
    }

    /// <summary>Build a query from text, embedded on a node (optionally with an explicit model).</summary>
    public static VectorQuery FromText(string text, string? model = null, int k = 10)
    {
        return new VectorQuery { Text = text, Model = model, K = k };
    }

    /// <summary>Attach a metadata filter. Returns <c>this</c> for chaining.</summary>
    public VectorQuery WithFilter(IReadOnlyDictionary<string, string> filter)
    {
        Filter = filter;
        return this;
    }
}
