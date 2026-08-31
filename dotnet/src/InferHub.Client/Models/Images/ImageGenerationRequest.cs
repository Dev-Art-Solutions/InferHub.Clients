using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Images;

/// <summary>
/// A text-to-image request — <c>POST /v1/images/generations</c> synchronously, or
/// <c>POST /api/images/jobs</c> as a job. <b>The same body either way</b>: what differs is whether
/// the caller waits.
/// </summary>
/// <remarks>
/// <see cref="Options"/> is not serialized — those knobs travel as <c>X-InferHub-Image-*</c>
/// headers. <see cref="NegativePrompt"/> deliberately does not: it is the caller's own words, and a
/// header is the one part of a request that every proxy and access log in the path writes down.
/// </remarks>
public sealed class ImageGenerationRequest
{
    /// <summary>The recipe id, as the fleet's node names it — e.g. <c>sdxl</c>. Required.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>What to draw. Required, and never logged by this library.</summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>What to keep out of the picture. A body field, never a header.</summary>
    [JsonPropertyName("negative_prompt")]
    public string? NegativePrompt { get; set; }

    /// <summary>How many images, 1–4 by default (<c>Images:MaxBatch</c>). Absent means one.</summary>
    [JsonPropertyName("n")]
    public int? Count { get; set; }

    /// <summary>
    /// <c>"WIDTHxHEIGHT"</c>, e.g. <c>"1024x1024"</c>. Absent takes the recipe's own default, which
    /// is the honest answer: 1024 is right for SDXL and ruinous for SD 1.5, and the hub will not
    /// invent one.
    /// </summary>
    /// <remarks>
    /// Both sides must be 64–4096 and a multiple of 8 — every latent-diffusion pipeline downsamples
    /// by 8 — or the hub answers <c>400</c> naming <c>size</c> before a step runs. Whether the
    /// <em>recipe</em> supports a size is the worker's question and costs one round trip.
    /// </remarks>
    [JsonPropertyName("size")]
    public string? Size { get; set; }

    /// <summary>
    /// The RNG seed, for a reproducible render. Also settable as
    /// <see cref="ImageOptions.Seed"/>; this one wins, because the hub reads the body first.
    /// </summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>
    /// One of <see cref="ImageResponseFormats"/>. Leave it null: <c>b64_json</c> is the only value
    /// the hub serves, and asking for <c>url</c> is a <c>400</c> saying it stores nothing.
    /// </summary>
    [JsonPropertyName("response_format")]
    public string? ResponseFormat { get; set; }

    /// <summary>
    /// Steps, guidance, seed and seam repair. Sent as headers, not in this body — see
    /// <see cref="ImageOptions"/>.
    /// </summary>
    [JsonIgnore]
    public ImageOptions? Options { get; set; }

    /// <summary>Shorthand for the common call.</summary>
    /// <param name="model">Recipe id.</param>
    /// <param name="prompt">What to draw.</param>
    /// <param name="size">Optional <c>"WIDTHxHEIGHT"</c>.</param>
    /// <param name="count">Optional image count.</param>
    public static ImageGenerationRequest Create(string model, string prompt, string? size = null, int? count = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        return new ImageGenerationRequest
        {
            Model = model,
            Prompt = prompt,
            Size = size,
            Count = count
        };
    }
}
