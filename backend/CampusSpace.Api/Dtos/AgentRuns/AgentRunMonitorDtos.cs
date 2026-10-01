using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.AgentRuns;

/// <summary>
/// GET /api/agent-runs (Facilities Officer, UC23). Status is repeatable and each value may be a comma list. From/To are
/// campus dates (yyyy-MM-dd), inclusive, on the run's CreatedAt. Fallback filters on "any step fell back" (worker_fallback
/// or planner_fallback true). Search matches the request's purpose. Newest first by default.
/// </summary>
public record AgentRunsQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["createdAt", "durationMs", "status"];

    public string[]? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    [Range(1, long.MaxValue)] public long? RequestId { get; init; }
    public bool? Fallback { get; init; }

    /// <summary>The Status values split on commas, trimmed and de-duplicated.</summary>
    public IReadOnlyList<string> Statuses() => (Status ?? [])
        .SelectMany(s => s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .Distinct()
        .ToList();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var unknown = Statuses().Except(AgentRunStatuses.All).ToList();
        if (unknown.Count > 0)
            yield return new ValidationResult(
                $"Unknown status: {string.Join(", ", unknown)}. Use one of: {string.Join(", ", AgentRunStatuses.All)}.", [nameof(Status)]);
        if (From is { } from && To is { } to && from > to)
            yield return new ValidationResult("From must not be later than To.", [nameof(From)]);
        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}

/// <summary>
/// One run in the monitor list. Times are UTC. DurationMs is the stored wall time, which for a decided run includes the
/// officer's wait. FailureReason is cut to <see cref="FailureReasonMaxLength"/> characters (plus "…"). TotalTokens is the
/// sum of the steps' usage.total_tokens, null when no step reported usage. AnyFallback: an LLM step fell back to its stub.
/// </summary>
public record AgentRunListItemDto(
    Guid Id, long RequestId, string Purpose, int RevisionNo, string Status, string? Model,
    DateTime? StartedAt, DateTime? CompletedAt, int? DurationMs, string? FailureReason,
    int StepCount, int ToolCallCount, long? TotalTokens, bool AnyFallback, DateTime CreatedAt)
{
    public const int FailureReasonMaxLength = 200;
}

/// <summary>GET /api/agent-runs/metrics. From/To are campus dates, inclusive, on the run's CreatedAt; omitted means unbounded.</summary>
public record AgentRunMetricsQuery : IValidatableObject
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is { } from && To is { } to && from > to)
            yield return new ValidationResult("From must not be later than To.", [nameof(From)]);
    }
}

/// <summary>
/// Agent metrics for the runs created in [From, To] (plan §10.11, §16.3). Every rate and average comes with its
/// denominator, and is null when that denominator is 0 (never a made-up 0%).
/// </summary>
public record AgentRunMetricsDto(DateOnly? From, DateOnly? To, RunMetricsDto Runs, IReadOnlyList<AgentMetricsDto> Agents);

/// <summary>
/// Per-run metrics. ReachedGate: the run got to the human gate (status AwaitingApproval, Completed, Rejected or
/// Cancelled, or its latest decision is Approve, which covers a failed approval). InProgress: Queued, Running, or
/// Resuming for a revise. Finished = Total − InProgress; FailedBeforeGate = Finished − ReachedGate.
/// SuccessRate = ReachedGate ÷ Finished. Processing times are Σ step DurationMs without the finalize step (every plan
/// cycle of the run, no inter-step overhead). AvgTokensPerRun = TotalTokens ÷ RunsWithUsage. FallbackRate =
/// FallbackRuns ÷ LlmAttemptedRuns.
/// </summary>
public record RunMetricsDto(
    int Total, IReadOnlyDictionary<string, int> ByStatus,
    int InProgress, int Finished, int ReachedGate, int FailedBeforeGate, double? SuccessRate,
    ProcessingTimeDto ReachedGateProcessing, ProcessingTimeDto FailedBeforeGateProcessing,
    int RunsWithUsage, long TotalTokens, double? AvgTokensPerRun,
    int LlmAttemptedRuns, int FallbackRuns, double? FallbackRate);

/// <summary>Agent processing time of one bucket. Runs = runs with at least one counted step (the denominator).</summary>
public record ProcessingTimeDto(int Runs, int WithoutSteps, int? AvgMs, int? P95Ms);

/// <summary>
/// Per-agent metrics (GROUP BY AgentName). Each step has one mode: fallback, llm (a model call), skipped (an LLM agent
/// that needed no model call) or stub (also code steps and failed steps). Latency, FailureRate and LlmShare divide by
/// Steps; FallbackRate by LlmAttemptedSteps (llm + fallback); AvgTokensPerStep by StepsWithUsage.
/// </summary>
public record AgentMetricsDto(
    string Agent, int Steps, int? AvgMs, int? P95Ms,
    int FailedSteps, double? FailureRate,
    int LlmSteps, double? LlmShare, int SkippedSteps,
    int LlmAttemptedSteps, int FallbackSteps, double? FallbackRate,
    int StepsWithUsage, long TotalTokens, double? AvgTokensPerStep);
