using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Agent workflow rows for tests. No production code writes these tables until the poller (Phase 3.3), so tests insert
/// them directly. Each helper saves and returns the new row's id.
/// </summary>
public static class AgentRunTestData
{
    public static async Task<Guid> InsertRunAsync(
        CustomWebApplicationFactory factory, long requestId, string status = AgentRunStatuses.Queued, int revisionNo = 1,
        string? failureReason = null)
    {
        var run = new AgentRun
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            RevisionNo = revisionNo,
            Status = status,
            FailureReason = failureReason ?? (status == AgentRunStatuses.Failed ? "test failure" : null),
        };
        await SaveAsync(factory, db => db.AgentRuns.Add(run));
        return run.Id;
    }

    public static async Task<long> InsertStepAsync(
        CustomWebApplicationFactory factory, Guid runId, int sequence = 1, string agentName = "supervisor",
        string status = AgentStepStatuses.Succeeded)
    {
        var step = new AgentStep
        {
            RunId = runId, Sequence = sequence, AgentName = agentName, Status = status,
            InputJson = """{"task":"plan"}""", OutputJson = """{"plan":[]}""", DurationMs = 120,
        };
        await SaveAsync(factory, db => db.AgentSteps.Add(step));
        return step.Id;
    }

    public static async Task<long> InsertToolCallAsync(
        CustomWebApplicationFactory factory, long stepId, string toolName = "find_rooms", bool succeeded = true)
    {
        var call = new AgentToolCall
        {
            StepId = stepId, ToolName = toolName, ArgsJson = """{"min_capacity":45}""",
            ResultSummary = """{"rooms":2}""", Succeeded = succeeded, Error = succeeded ? null : "TOOL_ERROR", DurationMs = 30,
        };
        await SaveAsync(factory, db => db.AgentToolCalls.Add(call));
        return call.Id;
    }

    public static async Task<long> InsertValidationResultAsync(
        CustomWebApplicationFactory factory, Guid runId, string ruleCode = "V01", int attempt = 1, bool passed = true)
    {
        var result = new AgentValidationResult { RunId = runId, RuleCode = ruleCode, Attempt = attempt, Passed = passed };
        await SaveAsync(factory, db => db.ValidationResults.Add(result));
        return result.Id;
    }

    public static async Task<long> InsertDecisionAsync(
        CustomWebApplicationFactory factory, long requestId, Guid runId, long officerId,
        string decision = ApprovalDecisions.Approve, string? comment = null)
    {
        var row = new ApprovalDecision
        {
            RequestId = requestId, AgentRunId = runId, OfficerId = officerId, Decision = decision, Comment = comment,
            DecidedAt = DateTime.UtcNow,
        };
        await SaveAsync(factory, db => db.ApprovalDecisions.Add(row));
        return row.Id;
    }

    private static async Task SaveAsync(CustomWebApplicationFactory factory, Action<AppDbContext> add)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        add(db);
        await db.SaveChangesAsync();
    }
}
