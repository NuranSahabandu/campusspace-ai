namespace CampusSpace.Api.Models;

/// <summary>
/// Business entities with audit timestamps. AppDbContext.SaveChanges sets both in UTC.
/// </summary>
public interface ITimestamped
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
