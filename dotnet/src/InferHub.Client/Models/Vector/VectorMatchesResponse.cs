using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Vector;

/// <summary>Envelope returned by the <c>/query</c> and <c>/retrieve</c> data-plane endpoints.</summary>
internal sealed class VectorMatchesResponse
{
    [JsonPropertyName("matches")]
    public List<VectorMatch> Matches { get; set; } = new();
}
