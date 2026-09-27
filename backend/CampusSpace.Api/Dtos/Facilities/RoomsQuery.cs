using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Facilities;

/// <summary>
/// GET /api/rooms. Search matches code or name. Features is a comma-separated list of codes; a room must have ALL of them.
/// IncludeInactive is honoured for Facilities Officers only.
/// </summary>
public record RoomsQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["code", "name", "capacity", "building"];

    [Range(1, long.MaxValue)] public long? BuildingId { get; init; }
    [ValidRoomType] public string? Type { get; init; }
    [Range(1, int.MaxValue)] public int? MinCapacity { get; init; }
    [MaxLength(500)] public string? Features { get; init; }
    public bool IncludeInactive { get; init; }

    /// <summary>The Features codes, trimmed, lower-cased and without duplicates.</summary>
    public IReadOnlyList<string> FeatureCodes() => FeatureCodeList.Parse(Features);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}
