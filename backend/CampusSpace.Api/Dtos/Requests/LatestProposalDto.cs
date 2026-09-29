namespace CampusSpace.Api.Dtos.Requests;

/// <summary>
/// The summary of a request's current proposal, shown to its owner and to officers: the live (Draft or Issued) quote,
/// its agent run and the proposed room. Null when the request has no live quote (processing, revised, rejected,
/// cancelled). Data minimisation: never the trace, the plan or the policy snapshot (those are officer-only, on
/// GET /api/agent-runs/{id}). RoomCode/RoomName come from Rooms, not from the agent's text.
/// </summary>
public record LatestProposalDto(
    Guid RunId, int RevisionNo, long? RoomId, string? RoomCode, string? RoomName,
    long QuoteId, decimal Total, bool Exempt, string QuoteStatus);
