namespace CampusSpace.Api.Models;

/// <summary>
/// The only list of equipment categories. Used by the EquipmentTypes.Category CHECK constraint and by request validation.
/// </summary>
public static class EquipmentCategories
{
    public const string Audio = "Audio";
    public const string Visual = "Visual";
    public const string Computing = "Computing";
    public const string Presentation = "Presentation";
    public const string Accessory = "Accessory";

    public static readonly IReadOnlyList<string> All = [Audio, Visual, Computing, Presentation, Accessory];
}
