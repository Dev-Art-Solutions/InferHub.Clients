namespace InferHub.Client.Models.Images;

/// <summary>
/// Change a picture — <c>POST /v1/images/edits</c>, or <c>POST /api/images/jobs</c> as a job.
/// A picture, a prompt, and optionally a mask saying which part to change.
/// </summary>
/// <remarks>
/// <para>
/// <b>An edit with no mask is image-to-image</b>: the whole picture moves toward the prompt, by
/// <see cref="ImageOptions.Strength"/>. With a mask, only the masked area is redrawn — and
/// <see cref="ImageOptions.MaskConvention"/> decides which pixels that is, because OpenAI and
/// <c>diffusers</c> mean opposite things by a mask.
/// </para>
/// <para>
/// The streams are read by the client and are <b>not disposed</b> by it. <b>The file name never
/// leaves the process</b>: the part travels under its role — <c>image</c>, <c>mask</c> — because
/// what somebody called a file on their disk is metadata about their day.
/// </para>
/// </remarks>
public sealed class ImageEditRequest
{
    /// <summary>The recipe id. Required.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>What to change. Required — an edit says what to change, and the hub refuses one without it.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>What to keep out. Optional.</summary>
    public string? NegativePrompt { get; set; }

    /// <summary>The picture to work from. Required, read but not disposed.</summary>
    public Stream? Image { get; set; }

    /// <summary>The media type of <see cref="Image"/> — e.g. <c>image/png</c>.</summary>
    public string? ImageContentType { get; set; }

    /// <summary>
    /// Which part to change, read according to <see cref="ImageOptions.MaskConvention"/>. Optional;
    /// without it the whole picture is edited.
    /// </summary>
    public Stream? Mask { get; set; }

    /// <summary>The media type of <see cref="Mask"/>.</summary>
    public string? MaskContentType { get; set; }

    /// <summary>How many images. Absent means one.</summary>
    public int? Count { get; set; }

    /// <summary><c>"WIDTHxHEIGHT"</c>. Absent keeps the input's own size.</summary>
    public string? Size { get; set; }

    /// <summary>One of <see cref="ImageResponseFormats"/>. Leave it null.</summary>
    public string? ResponseFormat { get; set; }

    /// <summary>Steps, guidance, seed, <see cref="ImageOptions.Strength"/> and the mask convention.</summary>
    public ImageOptions? Options { get; set; }
}

/// <summary>
/// More of this picture — <c>POST /v1/images/variations</c>, or <c>POST /api/images/jobs</c> as a
/// job. A picture and nothing else.
/// </summary>
/// <remarks>
/// <b>A variation takes no prompt and no mask, and this type is why you cannot send one.</b> The hub
/// refuses both with a <c>400</c> — "a variation takes no prompt — it is 'more of this picture'" and
/// "a variation takes no mask" — because "more of this" and "change this" are different requests and
/// only one of them has words in it. A single request record with nullable fields would make both
/// refusals expressible in C# and discoverable only over the network. For image-to-image with a
/// prompt, use <see cref="ImageEditRequest"/> with no mask.
/// </remarks>
public sealed class ImageVariationRequest
{
    /// <summary>The recipe id. Required.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>The picture to vary. Required, read but not disposed.</summary>
    public Stream? Image { get; set; }

    /// <summary>The media type of <see cref="Image"/> — e.g. <c>image/png</c>.</summary>
    public string? ImageContentType { get; set; }

    /// <summary>How many images. Absent means one.</summary>
    public int? Count { get; set; }

    /// <summary><c>"WIDTHxHEIGHT"</c>. Absent keeps the input's own size.</summary>
    public string? Size { get; set; }

    /// <summary>One of <see cref="ImageResponseFormats"/>. Leave it null.</summary>
    public string? ResponseFormat { get; set; }

    /// <summary>Steps, guidance, seed and <see cref="ImageOptions.Strength"/>.</summary>
    public ImageOptions? Options { get; set; }
}
