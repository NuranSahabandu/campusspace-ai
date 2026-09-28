namespace CampusSpace.Api.Models;

/// <summary>The officer's choices at the approval gate (§10, UC19–UC21). Reject and Revise need a comment.</summary>
public static class ApprovalDecisions
{
    public const string Approve = "Approve";
    public const string Reject = "Reject";
    public const string Revise = "Revise";

    public static readonly IReadOnlyList<string> All = [Approve, Reject, Revise];
}
