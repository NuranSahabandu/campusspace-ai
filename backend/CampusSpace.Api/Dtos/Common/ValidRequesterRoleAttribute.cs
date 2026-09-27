using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.Common;

/// <summary>The value must be one of RequesterRoles.All (null is left to [Required]).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidRequesterRoleAttribute : ValidationAttribute
{
    public ValidRequesterRoleAttribute() : base($"Requester role must be one of: {string.Join(", ", RequesterRoles.All)}.") { }

    public override bool IsValid(object? value) => value is null || (value is string role && RequesterRoles.All.Contains(role));
}
