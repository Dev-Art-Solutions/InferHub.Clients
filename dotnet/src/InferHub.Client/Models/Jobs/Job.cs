using InferHub.Client.Models.Ollama;

namespace InferHub.Client.Models.Jobs;

/// <summary>The statuses a text job moves through.</summary>
public static class JobStatuses
{
    /// <summary>Accepted, at the hub: waiting for admission, a place on a node, or a group to come together.</summary>
    public const string Queued = "queued";

    /// <summary>Sent to a node (or a provider).</summary>
    public const string Running = "running";

    /// <summary>Finished; the answer is at the result route.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>Finished without an answer; <see cref="Job.Error"/> says why.</summary>
    public const string Failed = "failed";

    /// <summary>Stopped because its submitter asked.</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>The stages inside a job's status.</summary>
public static class JobStages
{
    /// <summary>At the hub; <see cref="Job.Detail"/> says for what.</summary>
    public const string Waiting = "waiting";

    /// <summary>Sent to a node that has not said it has it yet.</summary>
    public const string Sent = "sent";

    /// <summary>The node has it and its engine has produced nothing yet: loading the model, reading the prompt.</summary>
    public const string Preparing = "preparing";

    /// <summary>The answer is being produced.</summary>
    public const string Generating = "generating";

    /// <summary>The job is finished.</summary>
    public const string Done = "done";
}

/// <summary>
/// A text job's status (<c>/api/jobs</c>, hub v3.103+): names, counts and times — never the
/// request or the answer.
/// </summary>
public sealed class Job
{
    /// <summary>The id to follow the job by.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>chat</c> or <c>generate</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The model the request named.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>One of <see cref="JobStatuses"/>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>One of <see cref="JobStages"/>.</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>A sentence about the stage: what a waiting job waits for, where a group's load is.</summary>
    public string? Detail { get; set; }

    /// <summary>When the hub accepted the job.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When it was first sent to a node.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When anything about it last changed.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>When it finished.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The node it was sent to; <c>null</c> while it waits, and for a job a provider answered.</summary>
    public JobNode? Node { get; set; }

    /// <summary>Set when something other than a node answered (<c>provider:&lt;id&gt;</c>).</summary>
    public string? ServedBy { get; set; }

    /// <summary>How many times it was sent to a node.</summary>
    public int Attempts { get; set; }

    /// <summary>Pieces of the answer received so far — a count that moves while the job runs.</summary>
    public int Pieces { get; set; }

    /// <summary>The answer's tokens, exact; arrives with the last piece.</summary>
    public int? Tokens { get; set; }

    /// <summary>Pieces per second while it runs; the engine's own tokens per second once it is finished.</summary>
    public double? TokensPerSecond { get; set; }

    /// <summary>Present when the model is one model split across several nodes.</summary>
    public JobGroup? Group { get; set; }

    /// <summary>Why a failed or cancelled job ended.</summary>
    public JobError? Error { get; set; }

    /// <summary>The submitting client's id — only on the admin listing.</summary>
    public string? Client { get; set; }

    /// <summary><see langword="true"/> once the job is succeeded, failed or cancelled.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTerminal => Status is JobStatuses.Succeeded or JobStatuses.Failed or JobStatuses.Cancelled;
}

/// <summary>The node a job was sent to.</summary>
public sealed class JobNode
{
    /// <summary>The node's stable id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The node's name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// When the node last said it is working on the job. It reports every couple of seconds, so a
    /// stale value means the node went quiet, not the model. <c>null</c> from a node older than v3.103.
    /// </summary>
    public DateTimeOffset? ReportedAt { get; set; }

    /// <summary>The pieces the node had produced at its last report.</summary>
    public int? Pieces { get; set; }
}

/// <summary>The group of nodes a split model runs on, as the hub sees it when the status is read.</summary>
public sealed class JobGroup
{
    /// <summary>The group's name — the model name the job asked for.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The node that runs the group's head.</summary>
    public string? Head { get; set; }

    /// <summary>Whether the group answers requests now.</summary>
    public bool Ready { get; set; }

    /// <summary>What the head says: <c>waiting</c>, <c>starting</c>, <c>running</c>, <c>failed</c>.</summary>
    public string? State { get; set; }

    /// <summary>Why the group is not ready.</summary>
    public string? Why { get; set; }

    /// <summary>The members and, while the group starts, how far each one's load is.</summary>
    public IReadOnlyList<JobGroupMember> Members { get; set; } = [];

    /// <summary>
    /// How much of the members' shares of the model has been sent, 0–1; <c>null</c> when the group
    /// does not say (it is running, or its head is older than v3.102).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double? LoadedFraction
    {
        get
        {
            long expect = 0, sent = 0;

            foreach (var member in Members)
            {
                if (member.ExpectMiB is not { } share)
                {
                    continue;
                }

                expect += share;
                sent += Math.Min(member.SentMiB ?? 0, share);
            }

            return expect > 0 ? (double)sent / expect : null;
        }
    }
}

/// <summary>One member of a split model's group.</summary>
public sealed class JobGroupMember
{
    /// <summary>The member node's name.</summary>
    public string Node { get; set; } = string.Empty;

    /// <summary>How many of the model's layers it holds.</summary>
    public int? Layers { get; set; }

    /// <summary>Its share of the model file, in MiB. <c>null</c> for the head's own worker.</summary>
    public long? ExpectMiB { get; set; }

    /// <summary>How much of that share the head has sent it since the group started.</summary>
    public long? SentMiB { get; set; }
}

/// <summary>Why a job ended without an answer.</summary>
public sealed class JobError
{
    /// <summary>The HTTP status the blocking route would have answered.</summary>
    public int? Status { get; set; }

    /// <summary><c>refused</c>, <c>node_error</c>, <c>node_lost</c>, <c>deadline</c>, <c>cancelled</c> or <c>hub_error</c>.</summary>
    public string? Code { get; set; }

    /// <summary>The hub's sentence.</summary>
    public string? Message { get; set; }

    /// <summary>The <c>Retry-After</c> the refusal carried, if it had one.</summary>
    public int? RetryAfterSeconds { get; set; }
}

/// <summary>
/// A job's answer. While the job still runs <see cref="Done"/> is <see langword="false"/> and
/// <see cref="Chat"/> / <see cref="Generate"/> hold the text so far — what a caller that reconnects
/// reads. Exactly one of the two is set, by the job's kind.
/// </summary>
public sealed class JobResult
{
    /// <summary><see langword="true"/> when this is the finished answer.</summary>
    public bool Done { get; init; }

    /// <summary>The answer of a <c>chat</c> job.</summary>
    public ChatResponse? Chat { get; init; }

    /// <summary>The answer of a <c>generate</c> job.</summary>
    public GenerateResponse? Generate { get; init; }

    /// <summary>Who answered (<c>X-InferHub-Served-By</c>); surfaced, never interpreted.</summary>
    public string? ServedBy { get; init; }

    /// <summary>The answer's text, whichever kind the job is.</summary>
    public string Text => Chat?.Message?.Content ?? Generate?.Response ?? string.Empty;
}

/// <summary>How <see cref="IInferHubJobsClient.WaitAsync"/> polls.</summary>
public sealed class JobWaitOptions
{
    /// <summary>Time between polls. One second by default.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}

internal sealed class JobList
{
    public List<Job> Jobs { get; set; } = [];
}

internal sealed record ChatJobSubmission(string Kind, ChatRequest Request);

internal sealed record GenerateJobSubmission(string Kind, GenerateRequest Request);
