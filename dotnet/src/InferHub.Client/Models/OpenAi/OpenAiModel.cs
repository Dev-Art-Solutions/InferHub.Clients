using System.Text.Json.Serialization;

namespace InferHub.Client.Models.OpenAi;

/// <summary>One model as <c>/v1/models</c> reports it.</summary>
public sealed class OpenAiModel
{
    /// <summary>Model name — the value to send as <c>model</c>.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Always <c>model</c>.</summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>Unix seconds; the hub stamps the time it answered, not a build date.</summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>Always <c>inferhub</c>.</summary>
    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; set; }

    /// <summary>
    /// What the fleet will actually do with this model — <c>chat</c>, <c>embed</c>, <c>stt</c> and
    /// so on. An InferHub extension to the OpenAI object, and <c>null</c> rather than <c>[]</c>
    /// when nothing can serve it. A model only a cloud provider holds reports <c>["chat"]</c>,
    /// because that is all a provider-served request can be.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string>? Capabilities { get; set; }
}

/// <summary>The envelope <c>GET /v1/models</c> returns.</summary>
public sealed class OpenAiModelList
{
    /// <summary>Always <c>list</c>.</summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>The models.</summary>
    [JsonPropertyName("data")]
    public IReadOnlyList<OpenAiModel> Data { get; set; } = Array.Empty<OpenAiModel>();
}
