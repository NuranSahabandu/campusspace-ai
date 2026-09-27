using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.Common;

/// <summary>The value must be one of EquipmentItemStatuses.All (null is left to [Required]).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidEquipmentStatusAttribute : ValidationAttribute
{
    public ValidEquipmentStatusAttribute() : base($"Status must be one of: {string.Join(", ", EquipmentItemStatuses.All)}.") { }

    public override bool IsValid(object? value) => value is null || (value is string v && EquipmentItemStatuses.All.Contains(v));
}
