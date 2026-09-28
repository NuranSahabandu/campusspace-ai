namespace CampusSpace.Api.Models;

/// <summary>A Facilities Officer's decision on a run's proposal (§8.1 Component D). Reject and Revise need a comment.</summary>
public class ApprovalDecision : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public BookingRequest Request { get; set; } = null!;
    public Guid AgentRunId { get; set; }
    public AgentRun AgentRun { get; set; } = null!;
    public long OfficerId { get; set; }
    public User Officer { get; set; } = null!;
    /// <summary>One of <see cref="ApprovalDecisions.All"/>.</summary>
    public string Decision { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
