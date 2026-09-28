namespace CampusSpace.Api.Models;

/// <summary>
/// One supervisor or worker call inside a run (the trace tree's middle level). Append-only, so not IAuditable.
/// InputJson/OutputJson are the task brief and the structured result: never hidden reasoning, raw tokens or secrets.
/// </summary>
public class AgentStep : ITimestamped
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public AgentRun Run { get; set; } = null!;
    public int Sequence { get; set; }
    public string AgentName { get; set; } = string.Empty;
    /// <summary>One of <see cref="AgentStepStatuses.All"/>.</summary>
    public string Status { get; set; } = AgentStepStatuses.Succeeded;
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public int Retries { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<AgentToolCall> ToolCalls { get; set; } = [];
}
