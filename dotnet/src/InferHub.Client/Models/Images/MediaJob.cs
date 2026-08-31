using System.Text.Json.Serialization;

namespace InferHub.Client.Models.Images;

/// <summary>
/// One asynchronous render, as the hub describes it: where it is in line, which step it is on, and
/// what there is to fetch.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is called <c>MediaJob</c> rather than <c>ImageJob</c> because the hub already serves video
/// from this same document.</b> <c>GET /api/videos/jobs</c> renders the identical fields, with
/// <see cref="Capability"/> telling them apart and <see cref="MediaJobOutput.Url"/> already pointing
/// at whichever content route belongs to that job. A type named for one modality would have to be
/// renamed the day the other arrives, and a published type is not renamed.
/// </para>
/// <para>
/// A job is <b>not a gallery</b>. Its bytes live five minutes by default and are dropped on
/// delivery, so a job whose images have been fetched is still listed, still says <c>succeeded</c>,
/// and has nothing left to fetch.
/// </para>
/// </remarks>
public sealed class MediaJob
{
    /// <summary>The job id — a GUID, and the capability: holding one is how the picture is fetched.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>One of <see cref="MediaJobStates"/>.</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>The recipe id this job was submitted against.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// <c>image</c>, <c>image-edit</c> or <c>video</c> — which surface this job belongs to. The
    /// routes are scoped by it: a video job's id on <c>GET /api/images/jobs/{id}</c> is a
    /// <c>404</c>, and the other way round.
    /// </summary>
    [JsonPropertyName("capability")]
    public string? Capability { get; set; }

    /// <summary>How many images were asked for.</summary>
    [JsonPropertyName("n")]
    public int? Count { get; set; }

    /// <summary>When the hub accepted it.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When a node picked it up. Null while it is still queued.</summary>
    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it reached a terminal state.</summary>
    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Place in line, 1-based, while queued. Null once it is running.</summary>
    [JsonPropertyName("queuePosition")]
    public int? QueuePosition { get; set; }

    /// <summary>Which node is rendering it. Reported, never interpreted.</summary>
    [JsonPropertyName("node")]
    public string? Node { get; set; }

    /// <summary>The step the worker is on. Null until it reports one.</summary>
    [JsonPropertyName("step")]
    public int? Step { get; set; }

    /// <summary>How many steps this render will take, as the worker declared it.</summary>
    [JsonPropertyName("totalSteps")]
    public int? TotalSteps { get; set; }

    /// <summary>
    /// What there is to fetch. <b>Present only once there is something</b> — so "is it ready" is
    /// answerable from the shape rather than from the state name plus a rule.
    /// </summary>
    [JsonPropertyName("images")]
    public IReadOnlyList<MediaJobOutput>? Images { get; set; }

    /// <summary>What this job was metered in — megapixel-steps, from what was produced rather than asked for.</summary>
    [JsonPropertyName("megapixelSteps")]
    public double? MegapixelSteps { get; set; }

    /// <summary>
    /// Whether the recipe's trigger was appended. Null means the recipe has no trigger, not that
    /// nothing happened.
    /// </summary>
    [JsonPropertyName("promptAugmented")]
    public bool? PromptAugmented { get; set; }

    /// <summary>The recipe's trigger constant, when it has one.</summary>
    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; }

    /// <summary>What the hub wants the caller to know about this render.</summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<string>? Warnings { get; set; }

    /// <summary>Why it ended as it did — <c>cancelled</c>, <c>read</c>, <c>retention</c>, <c>evicted</c>.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>The failure sentence, when it failed.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// The worker's own code for the failure, so a watching client can act on the <em>kind</em>
    /// without reading the sentence.
    /// </summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>Whether the work is over — succeeded, failed, cancelled or expired.</summary>
    [JsonIgnore]
    public bool IsTerminal => MediaJobStates.IsTerminal(State);

    /// <summary>
    /// Which node answered the request that produced this document, from
    /// <c>X-InferHub-Served-By</c> when the hub sent it.
    /// </summary>
    [JsonIgnore]
    public string? ServedBy { get; set; }
}

/// <summary>One output of a finished job — a picture, or a clip.</summary>
public sealed class MediaJobOutput
{
    /// <summary>Its index, which is what <c>OpenContentAsync</c> takes.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// Where the bytes are fetched from, as the hub itself names it — the images route for a
    /// picture, <c>/v1/videos/{id}/content</c> for a clip. <b>A pointer, not a promise it is still
    /// there</b>: the read unlinks it.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>The size it was produced at.</summary>
    [JsonPropertyName("size")]
    public string? Size { get; set; }

    /// <summary>The seed the worker used.</summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>How many bytes are waiting.</summary>
    [JsonPropertyName("bytes")]
    public long? Bytes { get; set; }

    /// <summary>How long the produced media runs, for a video. Null for a picture — absence is a fact.</summary>
    [JsonPropertyName("seconds")]
    public double? Seconds { get; set; }

    /// <summary>One of <see cref="ImageProjections"/>, declared by the worker.</summary>
    [JsonPropertyName("projection")]
    public string? Projection { get; set; }

    /// <summary>The seam measurement of these bytes, 0–1, where there is a seam to have.</summary>
    [JsonPropertyName("seamDelta")]
    public double? SeamDelta { get; set; }

    /// <summary>What that measurement said before a repair ran. Equal numbers are a real outcome.</summary>
    [JsonPropertyName("seamDeltaBefore")]
    public double? SeamDeltaBefore { get; set; }

    /// <summary>The repair mechanism that ran, when one was asked for.</summary>
    [JsonPropertyName("seamRepair")]
    public string? SeamRepair { get; set; }
}

/// <summary>
/// This client's jobs, with the queue's own numbers beside them — so a panel can say "3 waiting"
/// without counting rows it may not be allowed to see.
/// </summary>
/// <remarks>
/// The listing is <b>client-scoped</b>. It is not a fleet-wide view and no key makes it one.
/// </remarks>
public sealed class MediaJobList
{
    /// <summary>This client's jobs, oldest first.</summary>
    [JsonPropertyName("jobs")]
    public IReadOnlyList<MediaJob> Jobs { get; set; } = Array.Empty<MediaJob>();

    /// <summary>How many jobs are waiting, fleet-wide.</summary>
    [JsonPropertyName("queued")]
    public int Queued { get; set; }

    /// <summary>How many are running, fleet-wide.</summary>
    [JsonPropertyName("active")]
    public int Active { get; set; }

    /// <summary>How many bytes of finished results the hub is currently holding.</summary>
    [JsonPropertyName("retainedBytes")]
    public long RetainedBytes { get; set; }

    /// <summary>How long a finished job's bytes live, whether or not anybody reads them. 300 by default.</summary>
    [JsonPropertyName("retentionSeconds")]
    public int RetentionSeconds { get; set; }

    /// <summary>
    /// <c>none</c> or <c>file</c>. It changes what every other number here means: "held in memory,
    /// gone on restart" and "held for the window, restart or not" are different promises.
    /// </summary>
    [JsonPropertyName("persistence")]
    public string? Persistence { get; set; }
}

/// <summary>
/// The states a job moves through. Constants rather than an enum, for the reason every string
/// constant in this client is one: the hub may add a state in a release this package has not shipped
/// for, and an enum would turn "your hub is newer than your client" into a value that cannot be
/// represented.
/// </summary>
public static class MediaJobStates
{
    /// <summary>Accepted, in line, nothing spent. A queued job whose node vanishes is re-routed.</summary>
    public const string Queued = "queued";

    /// <summary>On a node. From here nothing is ever retried automatically.</summary>
    public const string Running = "running";

    /// <summary>A cancel has been asked for and the worker has not answered yet.</summary>
    public const string Cancelling = "cancelling";

    /// <summary>Done, and there are bytes to fetch — for five minutes, or until somebody reads them.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The worker refused or failed. <see cref="MediaJob.ErrorCode"/> says which kind.</summary>
    public const string Failed = "failed";

    /// <summary>Cancelled before it finished.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// The result is gone: retention lapsed, the byte ceiling evicted it, or it was read. Fetching
    /// its content is a <c>410</c> that says which — not a <c>404</c> that reads like a bug.
    /// </summary>
    public const string Expired = "expired";

    /// <summary>
    /// Whether the work is over. <see cref="Succeeded"/> is terminal and can still become
    /// <see cref="Expired"/>, which is a fact about the bytes rather than about the job.
    /// </summary>
    /// <param name="state">A <see cref="MediaJob.State"/> value.</param>
    public static bool IsTerminal(string? state) =>
        state is Succeeded or Failed or Cancelled or Expired;
}
