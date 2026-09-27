namespace CampusSpace.Api.Models;

/// <summary>
/// A kind of portable equipment (MIC-WIRELESS, PROJ-PORTABLE). Code is upper-case with hyphens, immutable after
/// creation, and is what the agents use. CoveredByFeatureCode names a room feature that makes this equipment
/// unnecessary (addendum Change B): the Equipment agent drops the line when the chosen room has that feature.
/// </summary>
public class EquipmentType : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>One of <see cref="EquipmentCategories.All"/>.</summary>
    public string Category { get; set; } = string.Empty;
    public decimal FeePerBooking { get; set; }
    public string? CoveredByFeatureCode { get; set; }
    public Feature? CoveredByFeature { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<EquipmentItem> Items { get; set; } = [];
    /// <summary>The types that can replace this one.</summary>
    public List<EquipmentSubstitute> Substitutes { get; set; } = [];
}
