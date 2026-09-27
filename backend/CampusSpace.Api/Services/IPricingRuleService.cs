using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Pricing;

namespace CampusSpace.Api.Services;

public interface IPricingRuleService
{
    Task<PagedResult<PricingRuleDto>> ListAsync(PricingRulesQuery query, CancellationToken ct = default);
    /// <summary>Every room type and requester role pair, with the rule in effect today or nulls.</summary>
    Task<IReadOnlyList<CurrentPricingRuleDto>> GetCurrentAsync(CancellationToken ct = default);
    Task<PricingRuleDto?> GetAsync(long id, CancellationToken ct = default);
    Task<PricingRuleDto> CreateAsync(PricingRuleRequest request, CancellationToken ct = default);
    /// <summary>Null if not found. A rule already in effect is a 400 on ValidFrom.</summary>
    Task<PricingRuleDto?> UpdateAsync(long id, PricingRuleRequest request, CancellationToken ct = default);
    /// <summary>False if not found. A rule already in effect is a 400 on ValidFrom.</summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// The rule that prices a booking starting at <paramref name="bookingStart"/>: the latest ValidFrom on or before
    /// the start's campus date. Null when none applies. The Phase 2 quotation calculation uses this.
    /// </summary>
    Task<EffectivePricingRule?> GetEffectiveRuleAsync(
        string roomType, string requesterRole, DateTimeOffset bookingStart, CancellationToken ct = default);
}
