using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Equipment;

public record EquipmentItemDto(
    long Id, string AssetTag, long TypeId, string TypeCode, string TypeName,
    string Condition, string Status, string? Notes, DateTime UpdatedAt);

/// <summary>
/// AssetTag is trimmed and upper-cased. TypeId cannot change after creation. Status OnLoan is set only by loans,
/// and an item on loan accepts only a Notes change. Condition Damaged requires Status UnderRepair or Retired.
/// </summary>
public record EquipmentItemRequest(
    [Required, MaxLength(30)] string AssetTag,
    [Range(1, long.MaxValue)] long TypeId,
    [Required, ValidEquipmentCondition] string Condition,
    [Required, ValidEquipmentStatus] string Status,
    [MaxLength(500)] string? Notes);

/// <summary>GET /api/equipment-items. Search matches the asset tag.</summary>
public record EquipmentItemsQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["assetTag", "type", "status", "condition", "updatedAt"];

    [Range(1, long.MaxValue)] public long? TypeId { get; init; }
    [ValidEquipmentStatus] public string? Status { get; init; }
    [ValidEquipmentCondition] public string? Condition { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}
