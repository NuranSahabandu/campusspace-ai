using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.Common;

/// <summary>The value must be one of EquipmentConditions.All (null is left to [Required]).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidEquipmentConditionAttribute : ValidationAttribute
{
    public ValidEquipmentConditionAttribute() : base($"Condition must be one of: {string.Join(", ", EquipmentConditions.All)}.") { }

    public override bool IsValid(object? value) => value is null || (value is string v && EquipmentConditions.All.Contains(v));
}
