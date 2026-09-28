using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampusSpace.Api.Agents;

/// <summary>
/// The agent service's /workflows JSON (agent-service/app/schemas.py): snake_case, and money as 2-dp strings
/// ("5500.00"), which AllowReadingFromString reads into decimals without a float in between.
/// </summary>
public static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}

/// <summary>WorkflowStatus in schemas.py.</summary>
public static class AgentWorkflowStatuses
{
    public const string Running = "running";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Completed = "completed";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>Decision in schemas.py (the body of POST /workflows/{thread_id}/resume).</summary>
public static class AgentDecisions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Revise = "revise";
    public const string Cancel = "cancel";
}

public sealed record StartWorkflowBody(Guid ThreadId, long RequestId);

public sealed record ResumeWorkflowBody(string Decision, string? Notes);

/// <summary>WorkflowAccepted: the 202 body of start and resume.</summary>
public sealed record AgentWorkflowAccepted(string ThreadId, string Status);

/// <summary>
/// WorkflowView: GET /workflows/{thread_id}. Trace fields are append-only on the agent side: step Sequence and
/// validation Attempt never reset within a thread, which is what makes the poller's copy idempotent.
/// </summary>
public sealed record AgentWorkflowView(
    string ThreadId,
    string Status,
    int Revision,
    JsonElement? Interrupt,
    JsonElement? Plan,
    JsonElement? Proposal,
    string? OfficerSummary,
    IReadOnlyList<AgentRuleRow>? Validation,
    IReadOnlyList<string>? Nodes,
    IReadOnlyList<AgentStepRow>? Steps,
    JsonElement? PolicySnapshot,
    string? Error,
    string? Model,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? DurationMs)
{
    /// <summary>The typed proposal (graph._proposal), or null when there is none yet.</summary>
    public AgentProposal? ReadProposal() =>
        Proposal is { ValueKind: JsonValueKind.Object } p ? p.Deserialize<AgentProposal>(AgentJson.Options) : null;
}

/// <summary>StepTrace in schemas.py.</summary>
public sealed record AgentStepRow(
    int Sequence,
    string AgentName,
    string Status,
    JsonElement? Input,
    JsonElement? Output,
    int Retries,
    string? Error,
    int DurationMs,
    IReadOnlyList<AgentToolCallRow>? ToolCalls);

/// <summary>ToolCallTrace in schemas.py. ResultSummary is already a summary, never the raw response.</summary>
public sealed record AgentToolCallRow(
    string ToolName,
    JsonElement? Args,
    JsonElement? ResultSummary,
    bool Succeeded,
    string? Error,
    int DurationMs);

/// <summary>RuleResult in schemas.py.</summary>
public sealed record AgentRuleRow(int Attempt, string Rule, bool Passed, string? Message);

/// <summary>
/// graph._proposal(): the chosen room (options[0] of the venue result), the equipment result and the Policy and Cost
/// quote. .NET never trusts the quote's numbers: the Draft quotation is recomputed by IQuotationCalculator.
/// </summary>
public sealed record AgentProposal(
    int Revision,
    long RoomId,
    string RoomCode,
    string RoomName,
    JsonElement? Venue,
    AgentEquipmentResult? Equipment,
    AgentQuote? Quote,
    IReadOnlyList<string>? PolicyFlags,
    string? OfficerSummary);

/// <summary>EquipmentResult in schemas.py.</summary>
public sealed record AgentEquipmentResult(
    IReadOnlyList<AgentEquipmentLine> Lines,
    IReadOnlyList<AgentSubstitution>? Substitutions,
    IReadOnlyList<string>? Unmet);

/// <summary>EquipmentLine: Source is portable, room_builtin (always Qty 0, addendum Change B) or substitute.</summary>
public sealed record AgentEquipmentLine(string TypeCode, int Qty, string Source)
{
    public const string RoomBuiltin = "room_builtin";
}

public sealed record AgentSubstitution(string RequestedCode, string SubstituteCode, int Qty, string Reason);

/// <summary>Quote in schemas.py. Every amount arrives as a 2-dp string.</summary>
public sealed record AgentQuote(
    IReadOnlyList<AgentQuoteLine> Lines,
    decimal Subtotal,
    decimal Discount,
    string? DiscountReason,
    bool Exempt,
    decimal Total,
    string? Currency);

public sealed record AgentQuoteLine(string Kind, string Description, decimal Qty, decimal UnitPrice, decimal LineTotal);
