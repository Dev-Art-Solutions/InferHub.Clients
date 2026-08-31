using System.Globalization;

namespace InferHub.Client.Models.Images;

/// <summary>
/// The <c>X-InferHub-Image-*</c> extension knobs, on every image request in either dialect.
/// </summary>
/// <remarks>
/// <para>
/// They are <b>headers</b> on the wire, not body fields — a body field would collide with whatever
/// OpenAI adds to its Images API next and would make a typed SDK's request object wrong, while a
/// header is additive by construction. That is the hub's decision; what this type adds is that
/// every number leaves here formatted with <see cref="CultureInfo.InvariantCulture"/>. A strength
/// of <c>0.75</c> sent as <c>0,75</c> from a Bulgarian or German machine is a <c>400</c> that only
/// reproduces on some developers' laptops.
/// </para>
/// <para>
/// <b>An unknown value is refused by the hub, never quietly ignored.</b> A caller whose seam repair
/// silently did not run sees a picture with a line in it and concludes the feature does not work.
/// </para>
/// </remarks>
public sealed class ImageOptions
{
    /// <summary>
    /// Denoising steps, 1–150. Absent takes the recipe's own default; a recipe may cap it lower.
    /// </summary>
    public int? Steps { get; set; }

    /// <summary>Classifier-free guidance, 0–50. Absent takes the recipe's default.</summary>
    public double? Guidance { get; set; }

    /// <summary>
    /// The RNG seed. Also settable in the body of a generation
    /// (<see cref="ImageGenerationRequest.Seed"/>); the body wins when both are given, which is the
    /// hub's own precedence and not this client's.
    /// </summary>
    public long? Seed { get; set; }

    /// <summary>
    /// How far an edit moves away from the picture it was given, 0–1. <b>Edits and variations
    /// only</b> — it is the whole knob of image-to-image, and OpenAI's edits API has none.
    /// </summary>
    /// <remarks>
    /// Absent takes the recipe's <c>defaults.strength</c>; a recipe with neither answers a
    /// <c>400</c> naming the header, because the hub cannot know what a recipe on a node defaults to.
    /// </remarks>
    public double? Strength { get; set; }

    /// <summary>
    /// Which way round a mask reads — one of <see cref="MaskConventions"/>. Edits only.
    /// </summary>
    public string? MaskConvention { get; set; }

    /// <summary>
    /// Close a panorama's join, and by which mechanism — one of <see cref="SeamRepairModes"/>.
    /// </summary>
    /// <remarks>
    /// Absent is byte-for-byte the hub's behaviour before it learned to repair seams, warnings
    /// included. <see cref="SeamRepairModes.Off"/> is the same request as absent.
    /// </remarks>
    public string? SeamRepair { get; set; }

    /// <summary>Shorthand for the two knobs a generation usually sets.</summary>
    /// <param name="steps">Denoising steps, 1–150.</param>
    /// <param name="seed">RNG seed, for a reproducible render.</param>
    public static ImageOptions For(int? steps = null, long? seed = null)
        => new() { Steps = steps, Seed = seed };

    /// <summary>
    /// The headers this object becomes, invariantly formatted. Empty when nothing is set, which is
    /// a request the hub answers exactly as it did before any of these knobs existed.
    /// </summary>
    internal IEnumerable<KeyValuePair<string, string>> ToHeaders()
    {
        if (Steps is { } steps)
        {
            yield return new(ImageHeaders.Steps, steps.ToString(CultureInfo.InvariantCulture));
        }

        if (Guidance is { } guidance)
        {
            yield return new(ImageHeaders.Guidance, guidance.ToString(CultureInfo.InvariantCulture));
        }

        if (Seed is { } seed)
        {
            yield return new(ImageHeaders.Seed, seed.ToString(CultureInfo.InvariantCulture));
        }

        if (Strength is { } strength)
        {
            yield return new(ImageHeaders.Strength, strength.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(MaskConvention))
        {
            yield return new(ImageHeaders.MaskConvention, MaskConvention);
        }

        if (!string.IsNullOrWhiteSpace(SeamRepair))
        {
            yield return new(ImageHeaders.SeamRepair, SeamRepair);
        }
    }
}

/// <summary>
/// The header names <see cref="ImageOptions"/> writes and <see cref="ImageContent"/> reads.
/// </summary>
public static class ImageHeaders
{
    /// <summary>Denoising steps — request.</summary>
    public const string Steps = "X-InferHub-Image-Steps";

    /// <summary>Classifier-free guidance — request.</summary>
    public const string Guidance = "X-InferHub-Image-Guidance";

    /// <summary>RNG seed — request.</summary>
    public const string Seed = "X-InferHub-Image-Seed";

    /// <summary>How far an edit moves from its input, 0–1 — request, edits only.</summary>
    public const string Strength = "X-InferHub-Image-Strength";

    /// <summary>Which way round the mask reads — request, edits only.</summary>
    public const string MaskConvention = "X-InferHub-Mask-Convention";

    /// <summary>Seam repair mechanism — request; and echoed on a fetched image that had one.</summary>
    public const string SeamRepair = "X-InferHub-Image-Seam-Repair";

    /// <summary>What the delivered bytes <em>are</em>, geometrically — response, on the content route.</summary>
    public const string Projection = "X-InferHub-Image-Projection";

    /// <summary>The seam measurement of the delivered bytes — response, only when a repair was asked for.</summary>
    public const string SeamDelta = "X-InferHub-Image-Seam-Delta";

    /// <summary>What that measurement said before the repair ran — response, same condition.</summary>
    public const string SeamDeltaBefore = "X-InferHub-Image-Seam-Delta-Before";
}

/// <summary>
/// Which pixels of a mask are the area to edit. Constants rather than an enum: the hub takes a
/// string and refuses an unknown one with a <c>400</c> naming the list.
/// </summary>
public static class MaskConventions
{
    /// <summary>The default — <b>transparent</b> pixels are the area to edit, as OpenAI's API defines it.</summary>
    public const string OpenAi = "openai";

    /// <summary><b>White</b> pixels are the area to edit, as <c>diffusers</c> defines it.</summary>
    public const string Luminance = "luminance";
}

/// <summary>
/// How a panorama's left-to-right join is closed, when the caller asks for it at all.
/// </summary>
public static class SeamRepairModes
{
    /// <summary>No repair. The same request as sending no header, and the default.</summary>
    public const string Off = "off";

    /// <summary>
    /// A wrapped feather across a narrow band at the join — milliseconds, no steps, nothing added to
    /// the bill. It closes a <em>tonal</em> discontinuity, not a structural one: a seam cutting
    /// through a doorway comes back with no step in brightness and the doorway still not lining up.
    /// </summary>
    public const string Blend = "blend";

    /// <summary>An inpainting pass over the join — slower, better, and billed as the steps it runs.</summary>
    public const string Diffuse = "diffuse";
}

/// <summary>
/// What a produced image <em>is</em>, geometrically. <b>Declared by the worker, never inferred from
/// the aspect ratio</b> — a 2048×1024 panorama and a 2048×1024 landscape photograph are the same
/// bytes in the same shape and are two completely different pictures.
/// </summary>
/// <remarks>
/// An unrecognised value is kept as it arrived rather than flattened to <see cref="Flat"/>: a future
/// worker's projection is the worker's to name.
/// </remarks>
public static class ImageProjections
{
    /// <summary>An ordinary picture. What every recipe reports unless it says otherwise.</summary>
    public const string Flat = "flat";

    /// <summary>A 360°×180° panorama: longitude across, latitude down, left edge continuing into the right. Always 2:1.</summary>
    public const string Equirectangular = "equirectangular";
}

/// <summary>
/// The <c>response_format</c> values the hub's Images API accepts — which is one.
/// </summary>
public static class ImageResponseFormats
{
    /// <summary>The image, base64, in the response body. The only supported value.</summary>
    public const string Base64 = "b64_json";

    /// <summary>
    /// OpenAI's other value, and the one InferHub refuses with a <c>400</c> that names the
    /// alternative: a URL means the hub keeps the bytes, and it does not keep anybody's pictures.
    /// Named here so the refusal is readable rather than mysterious.
    /// </summary>
    public const string Url = "url";
}
