using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.AgentTools;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Facilities;
using CampusSpace.Api.Dtos.Quotations;
using CampusSpace.Api.Middleware;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class AgentToolService(
    AppDbContext db,
    IBookingRequestService requests,
    IFeatureService features,
    IEquipmentTypeService equipmentTypes,
    IRoomService rooms,
    IRoomAvailabilityService roomAvailability,
    IEquipmentAvailabilityService equipmentAvailability,
    IQuotationCalculator calculator,
    IPolicySettingsService policy) : IAgentToolService
{
    public Task<AgentRequestContextDto?> GetRequestContextAsync(long requestId, CancellationToken ct = default) =>
        requests.GetAgentContextAsync(requestId, ct);

    public async Task<IReadOnlyList<AgentFeatureDto>> ListFeaturesAsync(CancellationToken ct = default) =>
        (await features.ListAsync(ct)).Select(f => new AgentFeatureDto(f.Code, f.Name)).ToList();

    public Task<IReadOnlyList<AgentEquipmentTypeDto>> ListEquipmentAsync(CancellationToken ct = default) =>
        equipmentTypes.ListCatalogAsync(ct);

    public Task<PagedResult<RoomDto>> FindAvailableRoomsAsync(AgentRoomsQuery query, CancellationToken ct = default)
    {
        var criteria = new AvailabilityCriteria(IsoInstant.Parse(query.Start), IsoInstant.Parse(query.End), query.MinCapacity!.Value,
            FeatureCodeList.Parse(query.Features), query.BuildingId, query.Type, query.MaxCapacity);
        return roomAvailability.FindAvailableAsync(criteria, query, ct);
    }

    public Task<RoomDto?> GetRoomAsync(long roomId, CancellationToken ct = default) => rooms.GetAsync(roomId, ct);

    public async Task<IReadOnlyList<AgentEquipmentAvailabilityDto>> GetEquipmentAvailabilityAsync(
        AgentEquipmentAvailabilityQuery query, CancellationToken ct = default)
    {
        var codes = query.CodeList;
        var types = await ResolveAsync(codes, nameof(AgentEquipmentAvailabilityQuery.Codes), ct);
        var (start, end) = (IsoInstant.Parse(query.Start), IsoInstant.Parse(query.End));

        var result = new List<AgentEquipmentAvailabilityDto>(codes.Count);
        foreach (var code in codes)
        {
            var type = types[code];
            // The type was just resolved, so the service finds it; a V05 slot error is its 400.
            var counts = (await equipmentAvailability.GetAvailabilityAsync(type.Id, start, end, ct))!;
            result.Add(new AgentEquipmentAvailabilityDto(counts.Code, counts.Name, counts.Serviceable, counts.Reserved,
                counts.Available, counts.OverAllocated, type.CoveredByFeatureCode));
        }
        return result;
    }

    public async Task<IReadOnlyList<AgentEquipmentRefDto>?> GetSubstitutesAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var typeId = await db.EquipmentTypes.Where(t => t.Code == normalized).Select(t => (long?)t.Id).SingleOrDefaultAsync(ct);
        if (typeId is null)
            return null;
        return (await equipmentTypes.GetSubstitutesAsync(typeId.Value, ct))!
            .Select(s => new AgentEquipmentRefDto(s.Code, s.Name)).ToList();
    }

    public async Task<QuotationDto?> QuoteAsync(AgentQuoteRequest request, CancellationToken ct = default)
    {
        var lines = request.Equipment ?? [];
        var types = await ResolveAsync(lines.Select(l => l.Code.Trim().ToUpperInvariant()).Distinct().ToList(),
            nameof(AgentQuoteRequest.Equipment), ct);
        var equipment = lines.Select(l => new QuoteEquipmentLine(types[l.Code.Trim().ToUpperInvariant()].Id, l.Quantity)).ToList();

        var quote = await calculator.CalculateAsync(new QuoteInput(request.RoomId!.Value, IsoInstant.Parse(request.Start),
            IsoInstant.Parse(request.End), request.RequesterRole!, equipment), ct);
        return quote is null ? null : new QuotationDto(null, null, null,
            quote.Lines.Select(l => new QuotationLineDto(l.Kind, l.Description, l.Qty, l.UnitPrice, l.LineTotal)).ToList(),
            quote.Subtotal, quote.Discount, quote.DiscountReason, quote.Exempt, quote.Total);
    }

    public async Task<IReadOnlyDictionary<string, object?>> GetPolicyAsync(CancellationToken ct = default) =>
        (await policy.GetAsync(ct)).ToPublicValues();

    private sealed record TypeRef(long Id, string? CoveredByFeatureCode);

    /// <summary>Upper-case codes to types. Any unknown code is one 400 on <paramref name="field"/>, listing them all.</summary>
    private async Task<Dictionary<string, TypeRef>> ResolveAsync(IReadOnlyList<string> codes, string field, CancellationToken ct)
    {
        if (codes.Count == 0)
            return [];
        var found = await db.EquipmentTypes.AsNoTracking().Where(t => codes.Contains(t.Code))
            .ToDictionaryAsync(t => t.Code, t => new TypeRef(t.Id, t.CoveredByFeatureCode), ct);
        var unknown = codes.Where(c => !found.ContainsKey(c)).ToList();
        if (unknown.Count > 0)
            throw new BusinessRuleException(field, $"Unknown equipment codes: {string.Join(", ", unknown)}.");
        return found;
    }
}
