namespace CampusSpace.Api.Models;

/// <summary>
/// Business entities with audit timestamps. AppDbContext.SaveChanges sets both in UTC.
/// </summary>
public interface ITimestamped : IHasUpdatedAt
{
    DateTime CreatedAt { get; set; }
}
