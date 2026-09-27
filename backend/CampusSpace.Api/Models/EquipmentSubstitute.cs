namespace CampusSpace.Api.Models;

/// <summary>Directional: the type <see cref="TypeId"/> can be replaced by <see cref="SubstituteTypeId"/>.</summary>
public class EquipmentSubstitute : ITimestamped, IAuditable
{
    public long TypeId { get; set; }
    public EquipmentType Type { get; set; } = null!;
    public long SubstituteTypeId { get; set; }
    public EquipmentType SubstituteType { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
