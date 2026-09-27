using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Equipment;

public record EquipmentTypeRefDto(long Id, string Code, string Name);

/// <summary>How many items of a type are in each status.</summary>
public record EquipmentItemCountsDto(int Total, int Available, int OnLoan, int UnderRepair, int Retired);

/// <summary>A list row. CoveredByFeatureName is null when CoveredByFeatureCode is.</summary>
public record EquipmentTypeDto(
    long Id, string Code, string Name, string Category, decimal FeePerBooking,
    string? CoveredByFeatureCode, string? CoveredByFeatureName, EquipmentItemCountsDto ItemCounts);

/// <summary>A list row plus the types that can replace this one (ordered by code).</summary>
public record EquipmentTypeDetailDto(
    long Id, string Code, string Name, string Category, decimal FeePerBooking,
    string? CoveredByFeatureCode, string? CoveredByFeatureName, EquipmentItemCountsDto ItemCounts,
    IReadOnlyList<EquipmentTypeRefDto> Substitutes);

/// <summary>
/// Code is trimmed and upper-cased, then must be letters/digits in hyphen-separated parts (MIC-WIRELESS), and cannot
/// change on update. CoveredByFeatureCode is trimmed and lower-cased; empty means none; otherwise it must exist in Features.
/// FeePerBooking has at most two decimal places.
/// </summary>
public record EquipmentTypeRequest(
    [Required, MinLength(2), MaxLength(40)] string Code,
    [Required, MaxLength(100)] string Name,
    [Required, ValidEquipmentCategory] string Category,
    [Required, Range(typeof(decimal), "0", "99999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    decimal? FeePerBooking,
    [MaxLength(50)] string? CoveredByFeatureCode);

/// <summary>Replaces the whole substitute set. Ids must exist, be distinct and not include the type itself.</summary>
public record ReplaceSubstitutesRequest([Required] long[] SubstituteTypeIds);

/// <summary>GET /api/equipment-types. Search matches code or name.</summary>
public record EquipmentTypesQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["code", "name", "category", "fee"];

    [ValidEquipmentCategory] public string? Category { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}
