namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of agent run statuses (§8.1 Agent workflow state, plus Resuming for the time between an officer's
/// decision and the graph picking it up, and Cancelled for a paused run ended by cancelling its request). Used by the AgentRuns CHECK and its one-live-run partial index.
/// </summary>
public static class AgentRunStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string AwaitingApproval = "AwaitingApproval";
    public const string Resuming = "Resuming";
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All =
        [Queued, Running, AwaitingApproval, Resuming, Completed, Rejected, Failed, Cancelled];

    /// <summary>No transition leaves these. A request may have any number of terminal runs.</summary>
    public static readonly IReadOnlyList<string> Terminal = [Completed, Rejected, Failed, Cancelled];

    /// <summary>A live run: at most one per request (IX_AgentRuns_RequestId_Live).</summary>
    public static readonly IReadOnlyList<string> Active = [Queued, Running, AwaitingApproval, Resuming];

    /// <summary>What the AgentRunPoller processes; an AwaitingApproval run waits for the officer and is not polled.</summary>
    public static readonly IReadOnlyList<string> Polled = [Queued, Running, Resuming];
}
