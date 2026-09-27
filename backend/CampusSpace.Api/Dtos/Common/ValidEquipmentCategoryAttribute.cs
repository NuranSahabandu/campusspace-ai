using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.Common;

/// <summary>The value must be one of EquipmentCategories.All (null is left to [Required]).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidEquipmentCategoryAttribute : ValidationAttribute
{
    public ValidEquipmentCategoryAttribute() : base($"Category must be one of: {string.Join(", ", EquipmentCategories.All)}.") { }

    public override bool IsValid(object? value) => value is null || (value is string v && EquipmentCategories.All.Contains(v));
}
