using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.Common;

/// <summary>The value must be one of Roles.All (null is left to [Required]).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidRoleAttribute : ValidationAttribute
{
    public ValidRoleAttribute() : base($"Role must be one of: {string.Join(", ", Roles.All)}.") { }

    public override bool IsValid(object? value) => value is null || (value is string role && Roles.All.Contains(role));
}
