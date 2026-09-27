namespace CampusSpace.Api.Models;

/// <summary>The only list of equipment item conditions (EquipmentItems.Condition CHECK and request validation).</summary>
public static class EquipmentConditions
{
    public const string Good = "Good";
    public const string MinorWear = "MinorWear";
    public const string Damaged = "Damaged";

    public static readonly IReadOnlyList<string> All = [Good, MinorWear, Damaged];
}
