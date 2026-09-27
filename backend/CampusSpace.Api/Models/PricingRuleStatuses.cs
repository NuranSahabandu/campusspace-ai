namespace CampusSpace.Api.Models;

/// <summary>
/// Where a pricing rule stands relative to campus today. Computed on read, never stored.
/// Current: the latest rule already in effect for its room type and role. Scheduled: starts after today.
/// Superseded: in effect once, but a later rule has since taken over.
/// </summary>
public static class PricingRuleStatuses
{
    public const string Current = "Current";
    public const string Scheduled = "Scheduled";
    public const string Superseded = "Superseded";
}
