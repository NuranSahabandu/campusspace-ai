using System.Text.Json;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class AgentRunReadService(
    AppDbContext db,
    IPolicySettingsService policy,
    ILogger<AgentRunReadService> logger) : IAgentRunReadService
{
    public async Task<IReadOnlyList<AgentRunSummaryDto>?> ListForRequestAsync(long requestId, CancellationToken ct = default)
    {
        if (!await db.BookingRequests.AnyAsync(r => r.Id == requestId, ct))
            return null;

        return await db.AgentRuns.AsNoTracking().Where(a => a.RequestId == requestId)
            .OrderByDescending(a => a.RevisionNo).ThenByDescending(a => a.CreatedAt)
            .Select(a => new AgentRunSummaryDto(
                a.Id, a.RevisionNo, a.Status, a.FailureReason, a.StartedAt, a.CompletedAt, a.DurationMs, a.Model, a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<AgentRunDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var run = await db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (run is null)
            return null;

        var steps = await db.AgentSteps.AsNoTracking().Where(s => s.RunId == id).OrderBy(s => s.Sequence)
            .Select(s => new
            {
                s.Sequence, s.AgentName, s.Status, s.Retries, s.Error, s.DurationMs, s.InputJson, s.OutputJson,
                ToolCalls = s.ToolCalls.OrderBy(t => t.Id)
                    .Select(t => new { t.ToolName, t.ArgsJson, t.ResultSummary, t.Succeeded, t.Error, t.DurationMs }).ToList(),
            })
            .ToListAsync(ct);

        var rules = await db.ValidationResults.AsNoTracking().Where(v => v.RunId == id)
            .OrderByDescending(v => v.Attempt).ThenBy(v => v.RuleCode)
            .Select(v => new { v.Attempt, v.RuleCode, v.Passed, v.Message })
            .ToListAsync(ct);

        var decisions = await db.ApprovalDecisions.AsNoTracking().Where(d => d.AgentRunId == id)
            .OrderBy(d => d.DecidedAt).ThenBy(d => d.Id)
            .Select(d => new ApprovalDecisionDto(d.Decision, d.Officer.FullName, d.Comment, d.DecidedAt))
            .ToListAsync(ct);

        var rooms = new Dictionary<long, ProposalRoomRef>();
        if (ProposalMapper.RoomId(run.ProposalJson) is { } roomId)
        {
            var room = await db.Rooms.AsNoTracking().Where(r => r.Id == roomId)
                .Select(r => new ProposalRoomRef(r.Id, r.Code, r.Name, r.Capacity, r.Building.Name))
                .SingleOrDefaultAsync(ct);
            if (room is not null)
                rooms[roomId] = room;
        }

        var changed = run.PolicySnapshotJson is null
            ? []
            : PolicyDiff.ChangedKeys(run.PolicySnapshotJson, (await policy.GetAsync(ct)).ToPublicValues());

        return new AgentRunDetailDto(
            run.Id, run.RequestId, run.RevisionNo, run.Status, run.Model, run.Nodes, run.OfficerSummary, run.FailureReason,
            run.CreatedAt, run.StartedAt, run.CompletedAt, run.DurationMs,
            ProposalMapper.Map(run.ProposalJson, rooms, logger, run.Id),
            steps.Select(s => new AgentStepDto(
                s.Sequence, s.AgentName, s.Status, s.Retries, s.Error, s.DurationMs, Json(s.InputJson), Json(s.OutputJson),
                s.ToolCalls.Select(t => new AgentToolCallDto(
                    t.ToolName, Json(t.ArgsJson) ?? EmptyObject, Json(t.ResultSummary), t.Succeeded, t.Error, t.DurationMs)).ToList()))
                .ToList(),
            rules.GroupBy(r => r.Attempt)
                .Select(g => new ValidationAttemptDto(g.Key, g.Select(r => new ValidationRuleDto(r.RuleCode, r.Passed, r.Message)).ToList()))
                .ToList(),
            decisions,
            Json(run.PolicySnapshotJson),
            changed);
    }

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>A stored jsonb value as JSON (not a string), so the client can pretty-print it.</summary>
    private static JsonElement? Json(string? json)
    {
        if (json is null)
            return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
