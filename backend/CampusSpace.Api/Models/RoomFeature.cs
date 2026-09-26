namespace CampusSpace.Api.Models;

/// <summary>Room ↔ Feature junction (M:N).</summary>
public class RoomFeature : ITimestamped, IAuditable
{
    public long RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public long FeatureId { get; set; }
    public Feature Feature { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
