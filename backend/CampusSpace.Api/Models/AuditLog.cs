namespace CampusSpace.Api.Models;

/// <summary>
/// One audited event (§15.3). Append-only, and never audited itself.
/// DetailsJson holds names and non-secret context only: never values of changed properties, passwords or tokens.
/// The one exception is PolicySetting rows, which record the old and new value of the changed key (addendum A.1):
/// booking policy is neither personal data nor a secret, and the audit trail must show what the rule was.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public User? User { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    /// <summary>Null for events without an entity, such as a failed login for an unknown email.</summary>
    public string? EntityId { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public DateTime At { get; set; }
}
