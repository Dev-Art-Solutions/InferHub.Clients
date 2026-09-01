using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Videos;

/// <summary>
/// A text-to-video request — <c>POST /v1/videos</c>. There is one route and it is asynchronous:
/// the hub answers with a queued <see cref="Video"/> and the render happens afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>One clip per request.</b> OpenAI's Videos API has no <c>n</c> and neither has this, so there
/// is no count here and the content route takes no index.
/// </para>
/// <para>
/// <see cref="Options"/> is not serialized — steps, guidance and seed travel as
/// <c>X-InferHub-Video-*</c> headers. <see cref="NegativePrompt"/> deliberately does not: it is the
/// caller's own words, and a header is the one part of a request that every proxy and access log in
/// the path writes down.
/// </para>
/// </remarks>
public sealed class VideoGenerationRequest
{
    /// <summary>The recipe id, as the fleet's node names it — e.g. <c>wan2.2</c>. Required.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>What to film. Required, and never logged by this library.</summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>What to keep out of the clip. A body field, never a header.</summary>
    [JsonPropertyName("negative_prompt")]
    public string? NegativePrompt { get; set; }

    /// <summary>
    /// <c>"WIDTHxHEIGHT"</c>, e.g. <see cref="VideoSizes.Wide480"/>. Absent takes the recipe's own
    /// default, which is the honest answer — 480p is right for one model and wrong for the next.
    /// </summary>
    /// <remarks>
    /// <b>Both sides must be a multiple of 16, not 8.</b> A video pipeline downsamples by 16 where
    /// an image pipeline downsamples by 8, so <c>1920x1080</c> — a perfectly good image size — is a
    /// <c>400</c> here and <c>1920x1088</c> is not. <see cref="VideoSizes"/> carries the sizes that
    /// pass and a validator; whether the <em>recipe</em> offers one is the worker's question.
    /// </remarks>
    [JsonPropertyName("size")]
    public string? Size { get; set; }

    /// <summary>
    /// How long the clip should run, in seconds. At most 60 at the edge, and the model you named
    /// will have a shorter list of its own. Absent takes the recipe's default.
    /// </summary>
    /// <remarks>
    /// The hub accepts both a number and OpenAI's own string spelling; this client sends a number.
    /// </remarks>
    [JsonPropertyName("seconds")]
    public double? Seconds { get; set; }

    /// <summary>
    /// The RNG seed, for a reproducible render. Also settable as <see cref="VideoOptions.Seed"/>;
    /// this one wins, because the hub reads the body first and only then the header.
    /// </summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>
    /// Steps and guidance, and a seed if you would rather set it there. Sent as headers, not in
    /// this body — see <see cref="VideoOptions"/>.
    /// </summary>
    [JsonIgnore]
    public VideoOptions? Options { get; set; }

    /// <summary>Shorthand for the common call.</summary>
    /// <param name="model">Recipe id.</param>
    /// <param name="prompt">What to film.</param>
    /// <param name="size">Optional <c>"WIDTHxHEIGHT"</c>, from <see cref="VideoSizes"/>.</param>
    /// <param name="seconds">Optional duration in seconds.</param>
    public static VideoGenerationRequest Create(string model, string prompt, string? size = null, double? seconds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        return new VideoGenerationRequest
        {
            Model = model,
            Prompt = prompt,
            Size = size,
            Seconds = seconds
        };
    }
}
