namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of agent run statuses (§8.1 Agent workflow state, plus Resuming for the time between an officer's
/// decision and the graph picking it up). Used by the AgentRuns CHECK and its one-live-run partial index.
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

    public static readonly IReadOnlyList<string> All =
        [Queued, Running, AwaitingApproval, Resuming, Completed, Rejected, Failed];

    /// <summary>No transition leaves these. A request may have any number of terminal runs.</summary>
    public static readonly IReadOnlyList<string> Terminal = [Completed, Rejected, Failed];

    /// <summary>A live run: at most one per request (IX_AgentRuns_RequestId_Live).</summary>
    public static readonly IReadOnlyList<string> Active = [Queued, Running, AwaitingApproval, Resuming];
}
