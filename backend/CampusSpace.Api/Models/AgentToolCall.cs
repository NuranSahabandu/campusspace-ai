namespace CampusSpace.Api.Models;

/// <summary>
/// One tool call made by a step. Append-only, so not IAuditable. ResultSummary is a summary of the tool's output,
/// not the raw response; no secrets or keys are ever stored. A failed call has an Error.
/// </summary>
public class AgentToolCall : ITimestamped
{
    public long Id { get; set; }
    public long StepId { get; set; }
    public AgentStep Step { get; set; } = null!;
    public string ToolName { get; set; } = string.Empty;
    public string ArgsJson { get; set; } = "{}";
    public string? ResultSummary { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
