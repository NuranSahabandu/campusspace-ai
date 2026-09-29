using System.Text.Json;

namespace CampusSpace.Api.Dtos.AgentRuns;

/// <summary>One agent run of a request (GET /api/booking-requests/{id}/agent-runs, newest first). Times are UTC.</summary>
public record AgentRunSummaryDto(
    Guid Id, int RevisionNo, string Status, string? FailureReason,
    DateTime? StartedAt, DateTime? CompletedAt, int? DurationMs, string? Model, DateTime CreatedAt);

/// <summary>
/// GET /api/agent-runs/{id} (Facilities Officer). Summaries, inputs, outputs and timings only. PolicyChangedKeys lists the
/// policy keys whose current value differs from PolicySnapshot (computed on the server, PolicyKeys order). Validation
/// is grouped by attempt, latest first. The agent's own quote is left out: prices come only from the .NET quotation.
/// </summary>
public record AgentRunDetailDto(
    Guid Id, long RequestId, int RevisionNo, string Status, string? Model, IReadOnlyList<string> Nodes,
    string? OfficerSummary, string? FailureReason,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt, int? DurationMs,
    AgentProposalDto? Proposal,
    IReadOnlyList<AgentStepDto> Steps,
    IReadOnlyList<ValidationAttemptDto> Validation,
    IReadOnlyList<ApprovalDecisionDto> Decisions,
    JsonElement? PolicySnapshot,
    IReadOnlyList<string> PolicyChangedKeys);

/// <summary>
/// The proposal. Chosen is the room of the proposal's room_id (with its option's reason when the agent listed it);
/// Alternatives are the other options (at most 2, or at most 3 when no option matches the chosen room).
/// </summary>
public record AgentProposalDto(
    VenueOptionDto Chosen,
    IReadOnlyList<VenueOptionDto> Alternatives,
    string? VenueUnmet,
    IReadOnlyList<ProposalEquipmentLineDto> EquipmentLines,
    IReadOnlyList<SubstitutionDto> Substitutions,
    IReadOnlyList<string> EquipmentUnmet,
    IReadOnlyList<string> PolicyFlags);

/// <summary>A room option. Reason is null for a chosen room the agent didn't list; Capacity/Building may then be null too.</summary>
public record VenueOptionDto(
    long RoomId, string Code, string Name, int? Capacity, string? Building, IReadOnlyList<string> Features, string? Reason);

/// <summary>Source is portable, room_builtin (always Qty 0, unpriced) or substitute.</summary>
public record ProposalEquipmentLineDto(string TypeCode, int Qty, string Source);

public record SubstitutionDto(string RequestedCode, string SubstituteCode, int Qty, string Reason);

public record AgentStepDto(
    int Sequence, string AgentName, string Status, int Retries, string? Error, int DurationMs,
    JsonElement? Input, JsonElement? Output, IReadOnlyList<AgentToolCallDto> ToolCalls);

public record AgentToolCallDto(
    string ToolName, JsonElement Args, JsonElement? ResultSummary, bool Succeeded, string? Error, int DurationMs);

public record ValidationAttemptDto(int Attempt, IReadOnlyList<ValidationRuleDto> Rules);

public record ValidationRuleDto(string Rule, bool Passed, string? Message);

/// <summary>An officer decision on this run. Comment is the officer's full text (untrusted; show as plain text).</summary>
public record ApprovalDecisionDto(string Decision, string OfficerName, string? Comment, DateTime DecidedAt);
