namespace CampusSpace.Api.Models;

/// <summary>
/// Table ValidationResults (§8.1; the class is named to avoid DataAnnotations.ValidationResult). One V01–V12 rule outcome for a run (§10.8). A run can be validated more than once (re-plans), so each pass has an
/// Attempt number; the latest checklist is the highest Attempt. Append-only, so not IAuditable.
/// </summary>
public class AgentValidationResult : ITimestamped
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public AgentRun Run { get; set; } = null!;
    public int Attempt { get; set; } = 1;
    public string RuleCode { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
