namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of equipment item statuses (EquipmentItems.Status CHECK and request validation).
/// OnLoan is set and cleared by loans (Phase 2), never through CRUD.
/// </summary>
public static class EquipmentItemStatuses
{
    public const string Available = "Available";
    public const string OnLoan = "OnLoan";
    public const string UnderRepair = "UnderRepair";
    public const string Retired = "Retired";

    public static readonly IReadOnlyList<string> All = [Available, OnLoan, UnderRepair, Retired];
}
