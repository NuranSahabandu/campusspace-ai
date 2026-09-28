using CampusSpace.Api.Dtos.AgentTools;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Dtos.Quotations;

namespace CampusSpace.Api.Services;

/// <summary>
/// The agents' read-only tools (§9 internal routes). The only place the tools compose the business services: each
/// method wraps an existing service (never re-implements it) and resolves equipment codes to type ids. Nothing here
/// saves. Rule breaks are BusinessRuleException (400); null means not found (404).
/// </summary>
public interface IAgentToolService
{
    Task<AgentRequestContextDto?> GetRequestContextAsync(long requestId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentFeatureDto>> ListFeaturesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AgentEquipmentTypeDto>> ListEquipmentAsync(CancellationToken ct = default);

    /// <summary>IRoomAvailabilityService.FindAvailableAsync: CheckSlot (V05) only, like the public endpoint.</summary>
    Task<PagedResult<RoomDto>> FindAvailableRoomsAsync(AgentRoomsQuery query, CancellationToken ct = default);

    /// <summary>Active rooms only: the agent has no officer role.</summary>
    Task<RoomDto?> GetRoomAsync(long roomId, CancellationToken ct = default);

    /// <summary>In the order the codes were given. An unknown code is a 400 on Codes.</summary>
    Task<IReadOnlyList<AgentEquipmentAvailabilityDto>> GetEquipmentAvailabilityAsync(
        AgentEquipmentAvailabilityQuery query, CancellationToken ct = default);

    /// <summary>The types that can replace <paramref name="code"/>, by code. Null when the code is unknown.</summary>
    Task<IReadOnlyList<AgentEquipmentRefDto>?> GetSubstitutesAsync(string code, CancellationToken ct = default);

    /// <summary>IQuotationCalculator with codes resolved to ids. Null when the room is unknown or inactive. Never saves.</summary>
    Task<QuotationDto?> QuoteAsync(AgentQuoteRequest request, CancellationToken ct = default);

    /// <summary>The full policy snapshot, as GET /api/policy-settings/public shows it.</summary>
    Task<IReadOnlyDictionary<string, object?>> GetPolicyAsync(CancellationToken ct = default);
}
