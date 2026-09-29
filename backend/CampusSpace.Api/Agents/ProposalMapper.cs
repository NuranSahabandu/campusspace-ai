using System.Text.Json;
using CampusSpace.Api.Dtos.AgentRuns;

namespace CampusSpace.Api.Agents;

/// <summary>A room as stored in Rooms, for a chosen room the agent didn't list among its options.</summary>
public sealed record ProposalRoomRef(long RoomId, string Code, string Name, int Capacity, string Building);

/// <summary>
/// Reads AgentRuns.ProposalJson (the view's "proposal" verbatim) into the officer's proposal DTO. The chosen room is the
/// proposal's room_id, never the order of venue.options: the option with that room_id is the chosen one and the others are
/// alternatives (at most 2). If no option matches, the chosen room comes from Rooms and every option is an alternative
/// (at most 3), with a warning. Lenient: a proposal that can't be read gives null and a warning, never an error.
/// </summary>
public static class ProposalMapper
{
    public const int MaxAlternatives = 2;
    public const int MaxOptions = 3;

    private sealed record VenueOptionRow(
        long RoomId, string? Code, string? Name, int? Capacity, string? Building, IReadOnlyList<string>? Features, string? Reason);

    private sealed record VenueRow(IReadOnlyList<VenueOptionRow>? Options, string? Unmet);

    /// <summary>The proposal's room_id, or null when there is no readable proposal.</summary>
    public static long? RoomId(string? proposalJson)
    {
        if (proposalJson is null)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(proposalJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("room_id", out var id) && id.TryGetInt64(out var roomId)
                ? roomId
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static AgentProposalDto? Map(
        string? proposalJson, IReadOnlyDictionary<long, ProposalRoomRef> rooms, ILogger logger, Guid runId)
    {
        if (proposalJson is null)
            return null;

        AgentProposal? proposal;
        VenueRow? venue;
        try
        {
            proposal = JsonSerializer.Deserialize<AgentProposal>(proposalJson, AgentJson.Options);
            venue = proposal?.Venue is { ValueKind: JsonValueKind.Object } v ? v.Deserialize<VenueRow>(AgentJson.Options) : null;
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Agent run {RunId} has a proposal that can't be read", runId);
            return null;
        }
        if (proposal is null)
            return null;

        var options = (venue?.Options ?? []).Where(o => o is not null).Take(MaxOptions).Select(ToDto).ToList();
        var chosenIndex = options.FindIndex(o => o.RoomId == proposal.RoomId);
        VenueOptionDto chosen;
        List<VenueOptionDto> alternatives;
        if (chosenIndex >= 0)
        {
            chosen = options[chosenIndex];
            alternatives = options.Where((_, i) => i != chosenIndex).Take(MaxAlternatives).ToList();
        }
        else
        {
            logger.LogWarning("Agent run {RunId}: the proposal's room {RoomId} is not among its venue options", runId, proposal.RoomId);
            var room = rooms.GetValueOrDefault(proposal.RoomId);
            chosen = new VenueOptionDto(
                proposal.RoomId, room?.Code ?? proposal.RoomCode ?? "", room?.Name ?? proposal.RoomName ?? "",
                room?.Capacity, room?.Building, [], null);
            alternatives = options;
        }

        var equipment = proposal.Equipment;
        return new AgentProposalDto(
            chosen, alternatives, venue?.Unmet,
            (equipment?.Lines ?? []).Select(l => new ProposalEquipmentLineDto(l.TypeCode, l.Qty, l.Source)).ToList(),
            (equipment?.Substitutions ?? []).Select(s => new SubstitutionDto(s.RequestedCode, s.SubstituteCode, s.Qty, s.Reason)).ToList(),
            equipment?.Unmet ?? [],
            proposal.PolicyFlags ?? []);
    }

    private static VenueOptionDto ToDto(VenueOptionRow o) =>
        new(o.RoomId, o.Code ?? "", o.Name ?? "", o.Capacity, o.Building, o.Features ?? [], o.Reason);
}
