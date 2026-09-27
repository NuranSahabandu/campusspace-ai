using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Pricing;

/// <summary>A rule and its status relative to campus today (one of PricingRuleStatuses).</summary>
public record PricingRuleDto(
    long Id, string RoomType, string RequesterRole, decimal HourlyRate, bool IsExempt, DateOnly ValidFrom, string Status);

/// <summary>
/// One room type and requester role, with the rule in effect today. The rule fields are null when the pair has no rule
/// in effect, so the UI can show the gap.
/// </summary>
public record CurrentPricingRuleDto(
    string RoomType, string RequesterRole, long? Id, decimal? HourlyRate, bool? IsExempt, DateOnly? ValidFrom);

/// <summary>
/// ValidFrom is a campus calendar date, today or later. HourlyRate has at most two decimal places and must be 0 when
/// IsExempt. Only rules that have not started yet can be changed.
/// </summary>
public record PricingRuleRequest(
    [Required, ValidRoomType] string RoomType,
    [Required, ValidRequesterRole] string RequesterRole,
    [Required, Range(typeof(decimal), "0", "99999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    decimal? HourlyRate,
    bool IsExempt,
    [Required] DateOnly? ValidFrom);

/// <summary>The rule that prices a booking (IPricingRuleService.GetEffectiveRuleAsync).</summary>
public record EffectivePricingRule(long Id, decimal HourlyRate, bool IsExempt, DateOnly ValidFrom);

/// <summary>
/// GET /api/pricing-rules. By default only rules in effect now and scheduled ones; IncludeHistory adds superseded
/// rules. Search matches the room type or role.
/// </summary>
public record PricingRulesQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["roomType", "requesterRole", "validFrom", "hourlyRate"];

    [ValidRoomType] public string? RoomType { get; init; }
    [ValidRequesterRole] public string? RequesterRole { get; init; }
    public bool IncludeHistory { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}
