namespace CampusSpace.Api.Models;

/// <summary>A bookable room. Deleting deactivates it (IsActive = false); rooms are never hard-deleted through the API.</summary>
public class Room : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public long BuildingId { get; set; }
    public Building Building { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>One of <see cref="RoomTypes.All"/>.</summary>
    public string Type { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<RoomFeature> RoomFeatures { get; set; } = [];
    public List<RoomBlackout> Blackouts { get; set; } = [];
}
