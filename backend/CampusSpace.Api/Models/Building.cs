namespace CampusSpace.Api.Models;

/// <summary>A campus building. Code is stored upper-case (MB, NB, EB).</summary>
public class Building : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Room> Rooms { get; set; } = [];
}
