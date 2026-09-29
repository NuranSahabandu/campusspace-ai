using CampusSpace.Api.Data;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Agents;

/// <summary>A room_builtin line (qty 0, addendum Change B) and the room feature that covers it (null if none or unknown type).</summary>
public sealed record BuiltinLine(string TypeCode, string? CoveredByFeatureCode);

/// <summary>
/// An agent proposal in .NET terms: the room, the portable lines as type ids (room_builtin lines left out), the builtin lines,
/// and .NET's own quote for the request's slot and requester role.
/// </summary>
public sealed record ResolvedProposal(
    long RoomId,
    IReadOnlyList<ReservationLine> PortableLines,
    IReadOnlyList<BuiltinLine> BuiltinLines,
    QuoteResult Quote,
    DateTimeOffset Start,
    DateTimeOffset End);

/// <summary>Either <see cref="Value"/> or <see cref="Error"/> (why the proposal can't be used).</summary>
public sealed record ProposalResolution(ResolvedProposal? Value, string? Error);

/// <summary>
/// Turns an agent proposal into type ids and a price. The poller uses it for the Draft quote and the approval finaliser
/// uses it for the reservations, the builtin check and the issued quote. The agent's own quote is never used (V09
/// compared it).
/// </summary>
public interface IProposalResolver
{
    Task<ProposalResolution> ResolveAsync(long requestId, AgentProposal? proposal, CancellationToken ct = default);
}

public sealed class ProposalResolver(AppDbContext db, IQuotationCalculator calculator) : IProposalResolver
{
    public const string NoProposalMessage = "The agent paused for approval without a proposal";

    public async Task<ProposalResolution> ResolveAsync(long requestId, AgentProposal? proposal, CancellationToken ct = default)
    {
        if (proposal is null)
            return new(null, NoProposalMessage);

        var request = await db.BookingRequests.AsNoTracking().Where(r => r.Id == requestId)
            .Select(r => new { r.RequestedStart, r.RequestedEnd, r.Requester.Role })
            .SingleAsync(ct);
        var all = proposal.Equipment?.Lines ?? [];
        // room_builtin lines are qty 0 and unpriced (addendum Change B); the calculator skips qty 0 too.
        var lines = all.Where(l => l.Qty > 0 && l.Source != AgentEquipmentLine.RoomBuiltin).ToList();
        var codes = lines.Select(l => l.TypeCode).Distinct().ToList();
        var typeIds = await db.EquipmentTypes.Where(t => codes.Contains(t.Code)).ToDictionaryAsync(t => t.Code, t => t.Id, ct);
        var unknown = codes.Where(c => !typeIds.ContainsKey(c)).ToList();
        if (unknown.Count > 0)
            return new(null, $"The proposal names unknown equipment types: {string.Join(", ", unknown)}");

        var builtinCodes = all.Where(l => l.Source == AgentEquipmentLine.RoomBuiltin).Select(l => l.TypeCode).Distinct().ToList();
        var covering = await db.EquipmentTypes.Where(t => builtinCodes.Contains(t.Code))
            .ToDictionaryAsync(t => t.Code, t => t.CoveredByFeatureCode, ct);
        var builtin = builtinCodes.Select(c => new BuiltinLine(c, covering.GetValueOrDefault(c))).ToList();

        var role = request.Role == Roles.Lecturer ? RequesterRoles.Lecturer : RequesterRoles.Student;
        var start = AgentTrace.Utc(request.RequestedStart);
        var end = AgentTrace.Utc(request.RequestedEnd);
        var portable = lines.Select(l => new ReservationLine(typeIds[l.TypeCode], l.Qty)).ToList();
        try
        {
            var quote = await calculator.CalculateAsync(new QuoteInput(
                proposal.RoomId, start, end, role,
                portable.Select(l => new QuoteEquipmentLine(l.TypeId, l.Quantity)).ToList()), ct);
            return quote is null
                ? new(null, "Quote could not be computed: the proposed room is unknown or inactive")
                : new(new ResolvedProposal(proposal.RoomId, portable, builtin, quote, start, end), null);
        }
        catch (BusinessRuleException ex)
        {
            return new(null, $"Quote could not be computed: {ex.Message}");
        }
    }
}
