namespace CampusSpace.Api.Models;

/// <summary>
/// One LangGraph run for a booking request (§8.1 Agent workflow state). Id is also the LangGraph thread_id: .NET
/// generates it (Guid.NewGuid) and never takes it from a client, because thread_id is an authorisation boundary.
/// Stores summaries, inputs, outputs and timings only: never model hidden reasoning, raw tokens or secrets.
/// PolicySnapshotJson is the policy the agents actually used, as reported by the agent service (addendum A.2).
/// </summary>
public class AgentRun : ITimestamped, IAuditable
{
    public Guid Id { get; set; }
    public long RequestId { get; set; }
    public BookingRequest Request { get; set; } = null!;
    public int RevisionNo { get; set; } = 1;
    /// <summary>One of <see cref="AgentRunStatuses.All"/>.</summary>
    public string Status { get; set; } = AgentRunStatuses.Queued;
    public string? PlanJson { get; set; }
    public string? ProposalJson { get; set; }
    public string? PolicySnapshotJson { get; set; }
    public string? OfficerSummary { get; set; }
    /// <summary>Required when Status is Failed.</summary>
    public string? FailureReason { get; set; }
    public string? Model { get; set; }
    /// <summary>The trajectory: node names in the order they ran.</summary>
    public List<string> Nodes { get; set; } = [];
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<AgentStep> Steps { get; set; } = [];
    public List<AgentValidationResult> ValidationResults { get; set; } = [];
}
