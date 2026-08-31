using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Images;

/// <summary>
/// What a synchronous image call answers — OpenAI's Images envelope, plus the fields InferHub adds
/// beside it.
/// </summary>
public sealed class ImageResponse
{
    /// <summary>Unix seconds, as OpenAI's API defines it.</summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>The images. One entry per <c>n</c>.</summary>
    [JsonPropertyName("data")]
    public IReadOnlyList<ImageData> Data { get; set; } = Array.Empty<ImageData>();

    /// <summary>
    /// Whether the recipe's trigger was appended to the prompt. <b>Present whether or not anything
    /// was appended</b>, for a recipe that has a trigger at all; <c>null</c> means the recipe has
    /// none, not that nothing happened.
    /// </summary>
    [JsonPropertyName("prompt_augmented")]
    public bool? PromptAugmented { get; set; }

    /// <summary>
    /// The recipe's trigger constant, when it has one. It is a fact about the model rather than the
    /// caller's words, which is why it is safe to surface and to log — the prompt never is.
    /// </summary>
    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; }

    /// <summary>What the hub wants the caller to know about this render. Absent when there is nothing.</summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<string>? Warnings { get; set; }

    /// <summary>
    /// Which node answered, from <c>X-InferHub-Served-By</c>. Reported, never interpreted — this
    /// client does not route, retry elsewhere, or prefer.
    /// </summary>
    [JsonIgnore]
    public string? ServedBy { get; set; }
}

/// <summary>One image out of a synchronous call.</summary>
public sealed class ImageData
{
    /// <summary>
    /// The image, base64. The only format the hub serves: a URL would mean it kept the bytes.
    /// Decode with <see cref="ToBytes"/>.
    /// </summary>
    [JsonPropertyName("b64_json")]
    public string? Base64 { get; set; }

    /// <summary>The size it was actually produced at — which a recipe may have clamped.</summary>
    [JsonPropertyName("size")]
    public string? Size { get; set; }

    /// <summary>The seed the worker used. Asking a caller to guess it would make the field useless.</summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>One of <see cref="ImageProjections"/>, declared by the worker.</summary>
    [JsonPropertyName("projection")]
    public string? Projection { get; set; }

    /// <summary>
    /// For an equirectangular render, how far its left and right columns are apart, 0–1, <b>as the
    /// bytes beside it stand</b>. Absent where there is no seam to have — a permanent zero would
    /// read as "perfectly seamless" rather than "not applicable".
    /// </summary>
    [JsonPropertyName("seam_delta")]
    public double? SeamDelta { get; set; }

    /// <summary>The mechanism that ran, when one was asked for — one of <see cref="SeamRepairModes"/>.</summary>
    [JsonPropertyName("seam_repair")]
    public string? SeamRepair { get; set; }

    /// <summary>
    /// What <see cref="SeamDelta"/> said before the repair ran. <b>Equal numbers are a real
    /// outcome</b>: the repair ran, it did not improve the seam, and the hub discarded it.
    /// </summary>
    [JsonPropertyName("seam_delta_before")]
    public double? SeamDeltaBefore { get; set; }

    /// <summary>
    /// OpenAI's own field, always <c>null</c> here: nothing in InferHub revises a prompt, and an
    /// augmentation is reported as <see cref="ImageResponse.Trigger"/> rather than by echoing the
    /// caller's own words back at them.
    /// </summary>
    [JsonPropertyName("revised_prompt")]
    public string? RevisedPrompt { get; set; }

    /// <summary>Decode <see cref="Base64"/>. Empty when there is nothing to decode.</summary>
    public byte[] ToBytes() =>
        string.IsNullOrEmpty(Base64) ? Array.Empty<byte>() : Convert.FromBase64String(Base64);
}
