namespace CampusSpace.Api.Models;

/// <summary>The only list of quotation line kinds. Used by the QuotationLines.Kind CHECK.</summary>
public static class QuotationLineKinds
{
    public const string Room = "Room";
    public const string Equipment = "Equipment";

    public static readonly IReadOnlyList<string> All = [Room, Equipment];
}
