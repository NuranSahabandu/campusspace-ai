using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.Common;

/// <summary>The value must be one of RoomTypes.All (null is left to [Required]).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ValidRoomTypeAttribute : ValidationAttribute
{
    public ValidRoomTypeAttribute() : base($"Type must be one of: {string.Join(", ", RoomTypes.All)}.") { }

    public override bool IsValid(object? value) => value is null || (value is string type && RoomTypes.All.Contains(type));
}
