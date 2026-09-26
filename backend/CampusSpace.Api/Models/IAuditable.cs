namespace CampusSpace.Api.Models;

/// <summary>
/// Marker for entities whose inserts, updates and deletes AppDbContext writes to AuditLogs automatically.
/// Only property names are recorded, never values.
/// </summary>
public interface IAuditable;
