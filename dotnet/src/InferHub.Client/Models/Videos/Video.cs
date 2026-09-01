using System.Globalization;
using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Videos;

/// <summary>
/// One clip, in OpenAI's own <c>video</c> shape — what <c>POST /v1/videos</c>,
/// <c>GET /v1/videos/{id}</c> and <c>DELETE</c>'s sibling routes answer with.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not <see cref="Images.MediaJob"/> renamed, and neither document is a rendering of the
/// other.</b> The hub runs both modalities through one job registry, and it describes that record
/// two ways: here, in the dialect every SDK speaks — a <c>video_…</c> id, a
/// <see cref="Status"/> word, an integer <see cref="Progress"/>, unix timestamps — and at
/// <c>GET /api/videos/jobs</c>, as the job document phase 10 already models, with a bare GUID, a
/// <c>state</c> and a step count. Mapping one onto the other would mean inventing values the hub
/// never sent. <see cref="VideoIdentifier"/> converts the ids, which is the only part that is
/// mechanical.
/// </para>
/// <para>
/// <b><see cref="Progress"/> never reaches 100 before the clip is finished.</b> The hub caps the
/// last step's frame at 99 on purpose: a caller who stops at 100 has stopped one round trip before
/// the bytes exist. Key on <see cref="IsTerminal"/> instead.
/// </para>
/// </remarks>
public sealed class Video
{
    /// <summary>
    /// The id, <c>video_&lt;32 hex&gt;</c>. Holding it <em>is</em> the capability to fetch the
    /// bytes, which is why this API has no listing: see <see cref="VideoErrorCodes.NotSupported"/>.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Always <c>video</c>.</summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>The recipe id this clip was submitted against.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>One of <see cref="VideoStatuses"/>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Per cent, 0–100 — but 100 only once the render is over. A queued clip reports 0, which is
    /// true rather than a placeholder: a queue position has nowhere to live in this shape and the
    /// hub does not invent one.
    /// </summary>
    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    /// <summary>When the hub accepted it, unix seconds.</summary>
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    /// <summary>When it reached a terminal state, unix seconds. Absent until then.</summary>
    [JsonPropertyName("completed_at")]
    public long? CompletedAt { get; set; }

    /// <summary>
    /// When the bytes stop being fetchable, unix seconds — the completion plus the hub's retention
    /// window. Absent while there is nothing to expire.
    /// </summary>
    /// <remarks>
    /// It is derived from the window the hub actually enforces, so planning around it is planning
    /// around the truth. It is still an upper bound and not a reservation: <b>reading the content
    /// expires it immediately</b>, whatever this says.
    /// </remarks>
    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; set; }

    /// <summary>
    /// The size it was produced at, <c>"WIDTHxHEIGHT"</c>. Absent while the hub does not know it —
    /// before the worker answers, the recipe's own default is the node's business.
    /// </summary>
    [JsonPropertyName("size")]
    public string? Size { get; set; }

    /// <summary>How long the clip runs, in seconds, as the worker measured it. Absent until then.</summary>
    [JsonPropertyName("seconds")]
    public double? Seconds { get; set; }

    /// <summary>
    /// Why it failed, when it did. <b>A failure arrives on a <c>200</c></b> — polling a failed clip
    /// is a successful request whose payload says <c>failed</c>, which is the normal way this
    /// dialect reports one.
    /// </summary>
    [JsonPropertyName("error")]
    public VideoError? Error { get; set; }

    /// <summary>When the hub accepted it, as a <see cref="DateTimeOffset"/>.</summary>
    [JsonIgnore]
    public DateTimeOffset Created => DateTimeOffset.FromUnixTimeSeconds(CreatedAt);

    /// <summary>When it finished, as a <see cref="DateTimeOffset"/>. Null until it does.</summary>
    [JsonIgnore]
    public DateTimeOffset? Completed =>
        CompletedAt is { } value ? DateTimeOffset.FromUnixTimeSeconds(value) : null;

    /// <summary>When the bytes stop being fetchable, as a <see cref="DateTimeOffset"/>.</summary>
    [JsonIgnore]
    public DateTimeOffset? Expires =>
        ExpiresAt is { } value ? DateTimeOffset.FromUnixTimeSeconds(value) : null;

    /// <summary>
    /// Whether the render is over — <c>completed</c> or <c>failed</c>. <b>Not the same as "there are
    /// bytes"</b>: a clip whose content has already been read is still <c>completed</c> and has
    /// nothing left to fetch.
    /// </summary>
    [JsonIgnore]
    public bool IsTerminal => VideoStatuses.IsTerminal(Status);

    /// <summary>
    /// Which node answered the request that produced this document, from
    /// <c>X-InferHub-Served-By</c> when the hub sent it. Reported, never interpreted.
    /// </summary>
    [JsonIgnore]
    public string? ServedBy { get; set; }
}

/// <summary>Why a clip failed, carried inside the <see cref="Video"/> rather than only on a status.</summary>
public sealed class VideoError
{
    /// <summary>The worker's own code, so a watching client can act on the kind without reading the sentence.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>The failure sentence.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>What <c>DELETE /v1/videos/{id}</c> answers.</summary>
public sealed class VideoDeletion
{
    /// <summary>The id that was deleted.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Always <c>video</c>.</summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>Always <c>true</c> — the hub answers a <c>404</c> rather than <c>deleted:false</c>.</summary>
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }
}

/// <summary>
/// The four status words this dialect has. Constants rather than an enum, for the reason every
/// string constant in this client is one: the hub may add a word in a release this package has not
/// shipped for, and an enum would turn "your hub is newer than your client" into a value that cannot
/// be represented.
/// </summary>
/// <remarks>
/// They are <b>not</b> <see cref="Images.MediaJobStates"/>. Two of that vocabulary's states are
/// deliberately not expressible here: a job that has been asked to stop reports
/// <see cref="InProgress"/> — it really is still running, and inventing a word OpenAI's clients do
/// not know would break the typed enums that are the point of adopting a dialect — and a clip whose
/// bytes are gone reports <see cref="Completed"/>, because the render did happen. The
/// <c>/content</c> route is where "gone" is said, as a <c>410</c>.
/// </remarks>
public static class VideoStatuses
{
    /// <summary>Accepted, in line, nothing spent.</summary>
    public const string Queued = "queued";

    /// <summary>On a node — or asked to stop and not yet answered.</summary>
    public const string InProgress = "in_progress";

    /// <summary>Done. There are bytes to fetch until somebody reads them or retention lapses.</summary>
    public const string Completed = "completed";

    /// <summary>The worker refused or failed. <see cref="Video.Error"/> says which kind.</summary>
    public const string Failed = "failed";

    /// <summary>Whether the render is over.</summary>
    /// <param name="status">A <see cref="Video.Status"/> value.</param>
    public static bool IsTerminal(string? status) => status is Completed or Failed;
}

/// <summary>
/// The <c>error.code</c> values this surface answers with, named so a <c>catch</c> can key on them.
/// </summary>
public static class VideoErrorCodes
{
    /// <summary>
    /// A route this dialect has and this hub refuses, as a <c>501</c> with the reason in the
    /// sentence: <b>remix</b> (nothing durable holds the prompt that made a clip — no prompt, no
    /// negative prompt, by design) and <b>listing</b> (a video id is itself the capability to fetch
    /// the bytes, so the API hands out no way to enumerate). Neither is a method on
    /// <see cref="IInferHubVideoClient"/>: the client cannot make either work, and a method that
    /// could only throw would read as "not implemented yet".
    /// </summary>
    public const string NotSupported = "not_supported";

    /// <summary>No such clip — or not yours, or an image job's id. The same <c>404</c> for all three.</summary>
    public const string NotFound = "video_not_found";

    /// <summary>The clip is queued, running or failed; there is nothing to fetch yet. A <c>409</c>.</summary>
    public const string NotReady = "video_not_ready";

    /// <summary>
    /// The bytes are gone — read, evicted, or retention lapsed. A <c>410</c>, and deliberately not a
    /// <c>404</c>: "you were too late" and "that never existed" are two problems with two fixes.
    /// </summary>
    public const string Expired = "video_expired";

    /// <summary>
    /// The fleet holds the model and no node is currently doing video work. A <c>503</c> carrying
    /// <c>Retry-After: 30</c>, and <b>not</b> the <c>404</c> an unknown model earns — one is worth
    /// retrying later and the other never is.
    /// </summary>
    public const string CapabilityUnavailable = "capability_unavailable";

    /// <summary>The queue is full. A <c>503</c> with a <c>Retry-After</c>.</summary>
    public const string QueueFull = "queue_full";
}

/// <summary>
/// The two spellings of one id. <c>/v1/videos</c> says <c>video_9d1f…</c>; the job listing at
/// <c>GET /api/videos/jobs</c> says the bare GUID, because it is phase 47's job document and always
/// was.
/// </summary>
/// <remarks>
/// A caller who found a clip in <see cref="IInferHubVideoClient.ListJobsAsync"/> and now wants its
/// bytes has the second and needs the first. Converting here beats each caller learning the prefix
/// by reading a 404.
/// </remarks>
public static class VideoIdentifier
{
    /// <summary>The prefix OpenAI-shaped video ids carry.</summary>
    public const string Prefix = "video_";

    /// <summary>
    /// The <c>video_…</c> id for a job id from <see cref="Images.MediaJob.Id"/>. Idempotent: an id
    /// that already carries the prefix is returned unchanged.
    /// </summary>
    /// <param name="jobId">A job id — a bare GUID, or an id that already carries the prefix.</param>
    public static string ToVideoId(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var trimmed = jobId.Trim();

        if (trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return trimmed;
        }

        return Guid.TryParse(trimmed, out var parsed)
            ? Prefix + parsed.ToString("N", CultureInfo.InvariantCulture)
            : Prefix + trimmed;
    }

    /// <summary>
    /// The bare job id behind a <c>video_…</c> id — what <c>GET /api/videos/jobs</c> rows carry.
    /// </summary>
    /// <param name="videoId">A <c>video_…</c> id, or a bare GUID.</param>
    public static string ToJobId(string videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);

        var trimmed = videoId.Trim();
        var body = trimmed.StartsWith(Prefix, StringComparison.Ordinal) ? trimmed[Prefix.Length..] : trimmed;

        return Guid.TryParse(body, out var parsed) ? parsed.ToString("D", CultureInfo.InvariantCulture) : body;
    }
}
