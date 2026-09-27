namespace CampusSpace.Api.Models;

/// <summary>
/// Entities with a last-changed timestamp. AppDbContext.SaveChanges sets it in UTC on insert and update.
/// PolicySetting has only this (addendum A.1: a natural key and no CreatedAt); business tables use ITimestamped.
/// </summary>
public interface IHasUpdatedAt
{
    DateTime UpdatedAt { get; set; }
}
