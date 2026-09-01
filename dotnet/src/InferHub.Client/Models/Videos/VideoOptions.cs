using System.Globalization;

namespace InferHub.Client.Models.Videos;

/// <summary>
/// The <c>X-InferHub-Video-*</c> extension knobs, on every <c>POST /v1/videos</c>.
/// </summary>
/// <remarks>
/// <para>
/// They are <b>headers</b> on the wire, not body fields — a body field would collide with whatever
/// OpenAI adds to its Videos API next. That is the hub's decision; what this type adds is that
/// every number leaves here formatted with <see cref="CultureInfo.InvariantCulture"/>. A guidance
/// of <c>5.5</c> sent as <c>5,5</c> from a Bulgarian or German machine is
/// <c>400 "X-InferHub-Video-Guidance: '5,5' is not a number between 0 and 50 (use a decimal
/// point)"</c> — a failure that only reproduces on some developers' laptops.
/// </para>
/// <para>
/// There is no <c>strength</c> and no mask here: the hub takes no picture into a video request, so
/// the knobs that describe "how far from the input" have no input to describe.
/// </para>
/// </remarks>
public sealed class VideoOptions
{
    /// <summary>Denoising steps, 1–150. Absent takes the recipe's own default; a recipe may cap it lower.</summary>
    public int? Steps { get; set; }

    /// <summary>Classifier-free guidance, 0–50. Absent takes the recipe's default.</summary>
    public double? Guidance { get; set; }

    /// <summary>
    /// The RNG seed. Also settable in the body (<see cref="VideoGenerationRequest.Seed"/>), and the
    /// body wins when both are given — the hub's own precedence, not this client's.
    /// </summary>
    public long? Seed { get; set; }

    /// <summary>Shorthand for the two knobs a render usually sets.</summary>
    /// <param name="steps">Denoising steps, 1–150.</param>
    /// <param name="seed">RNG seed, for a reproducible render.</param>
    public static VideoOptions For(int? steps = null, long? seed = null)
        => new() { Steps = steps, Seed = seed };

    /// <summary>
    /// The headers this object becomes, invariantly formatted. Empty when nothing is set, which is
    /// a request the hub answers exactly as it did before any of these knobs existed.
    /// </summary>
    internal IEnumerable<KeyValuePair<string, string>> ToHeaders()
    {
        if (Steps is { } steps)
        {
            yield return new(VideoHeaders.Steps, steps.ToString(CultureInfo.InvariantCulture));
        }

        if (Guidance is { } guidance)
        {
            yield return new(VideoHeaders.Guidance, guidance.ToString(CultureInfo.InvariantCulture));
        }

        if (Seed is { } seed)
        {
            yield return new(VideoHeaders.Seed, seed.ToString(CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>
/// The header names <see cref="VideoOptions"/> writes. <b>They are not the image ones</b>: the hub
/// reads <c>X-InferHub-Video-Steps</c> on this route and ignores <c>X-InferHub-Image-Steps</c>
/// entirely, so a copied image request renders at the recipe's default and says nothing about it.
/// </summary>
public static class VideoHeaders
{
    /// <summary>Denoising steps — request.</summary>
    public const string Steps = "X-InferHub-Video-Steps";

    /// <summary>Classifier-free guidance — request.</summary>
    public const string Guidance = "X-InferHub-Video-Guidance";

    /// <summary>RNG seed — request.</summary>
    public const string Seed = "X-InferHub-Video-Seed";
}
