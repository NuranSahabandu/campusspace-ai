namespace CampusSpace.Api.Models;

/// <summary>One physical, asset-tagged piece of equipment. Items are never deleted; they are retired (Status = Retired).</summary>
public class EquipmentItem : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public long TypeId { get; set; }
    public EquipmentType Type { get; set; } = null!;
    public string AssetTag { get; set; } = string.Empty;
    /// <summary>One of <see cref="EquipmentConditions.All"/>.</summary>
    public string Condition { get; set; } = EquipmentConditions.Good;
    /// <summary>One of <see cref="EquipmentItemStatuses.All"/>.</summary>
    public string Status { get; set; } = EquipmentItemStatuses.Available;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
