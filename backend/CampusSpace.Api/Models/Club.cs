namespace CampusSpace.Api.Models;

/// <summary>A student club. Deactivated with IsActive = false; never hard-deleted.</summary>
public class Club : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<ClubMember> Members { get; set; } = [];
}
