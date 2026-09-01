using System.Globalization;

namespace InferHub.Client.Models.Videos;

/// <summary>
/// Sizes a video request can ask for, and the rule that decides them — <b>which is not the image
/// rule</b>.
/// </summary>
/// <remarks>
/// <para>
/// A video pipeline downsamples by <see cref="Grid"/> where an image pipeline downsamples by 8, so
/// <c>1920x1080</c> — a fine image size, and the first one a caller reusing their image code
/// sends — is refused here:
/// </para>
/// <code>
/// size '1920x1080' must have both sides a multiple of 16 — a video pipeline downsamples by 16
/// where an image pipeline downsamples by 8, and this is one of the two grids that differ
/// </code>
/// <para>
/// <b>These constants make the common case right; the hub stays the authority.</b> This client does
/// not refuse a size locally: the recipe's own catalogue is shorter than the grid rule and only the
/// worker knows it, so a local refusal would reject requests a node would have served. Use
/// <see cref="IsValid"/> to check a size you built yourself before spending a round trip on it.
/// </para>
/// </remarks>
public static class VideoSizes
{
    /// <summary>Every latent video pipeline in the hub's pinned wheel downsamples by this.</summary>
    public const int Grid = 16;

    /// <summary>The smallest side the hub accepts.</summary>
    public const int MinDimension = 64;

    /// <summary>
    /// The largest side the hub accepts — lower than the image ceiling on purpose: 4096² across
    /// eighty frames is not a request any card in the catalogue can serve, and admitting it only
    /// moves the refusal to somewhere it costs a dispatch.
    /// </summary>
    public const int MaxDimension = 2048;

    /// <summary>16:9 at 480p — <c>832x480</c>, the size most video recipes are tuned for.</summary>
    public const string Wide480 = "832x480";

    /// <summary>1:1 at 480p — <c>480x480</c>.</summary>
    public const string Square480 = "480x480";

    /// <summary>Portrait 480p — <c>480x832</c>.</summary>
    public const string Portrait480 = "480x832";

    /// <summary>16:9 at 720p — <c>1280x720</c>. Both sides are multiples of 16.</summary>
    public const string Wide720 = "1280x720";

    /// <summary>
    /// 1080p's honest neighbour — <c>1920x1088</c>. <c>1920x1080</c> is a <c>400</c> here because
    /// <c>1080</c> is not a multiple of 16, and this is the nearest size that is.
    /// </summary>
    public const string Wide1088 = "1920x1088";

    /// <summary>
    /// Whether a <c>"WIDTHxHEIGHT"</c> string is one the hub's edge will accept — both sides
    /// <see cref="MinDimension"/>–<see cref="MaxDimension"/> and a multiple of <see cref="Grid"/>.
    /// </summary>
    /// <remarks>
    /// A <c>true</c> here is not a promise the recipe offers it: that list lives on the node and the
    /// hub's refusal names it.
    /// </remarks>
    /// <param name="size">A <c>"WIDTHxHEIGHT"</c> string, e.g. <c>"832x480"</c>.</param>
    public static bool IsValid(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return false;
        }

        var parts = size.Trim().ToLowerInvariant().Split('x');

        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height))
        {
            return false;
        }

        return width is >= MinDimension and <= MaxDimension
            && height is >= MinDimension and <= MaxDimension
            && width % Grid == 0
            && height % Grid == 0;
    }
}
