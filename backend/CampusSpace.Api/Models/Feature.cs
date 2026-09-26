namespace CampusSpace.Api.Models;

/// <summary>
/// A built-in room feature. Code is lower-case snake_case (projector, sound_system) and is what the agents use.
/// </summary>
public class Feature : ITimestamped, IAuditable
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
