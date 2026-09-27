namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of quotation statuses (§8.1). The agent's quote is saved as a Draft, the approval issues it, and a newer
/// draft voids the older one. <see cref="Live"/> quotes are the ones IX_Quotations_RequestId_Live allows once per request.
/// </summary>
public static class QuotationStatuses
{
    public const string Draft = "Draft";
    public const string Issued = "Issued";
    public const string Void = "Void";

    public static readonly IReadOnlyList<string> All = [Draft, Issued, Void];

    public static readonly IReadOnlyList<string> Live = [Draft, Issued];
}
