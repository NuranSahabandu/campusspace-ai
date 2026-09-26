using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.Users;

/// <summary>GET /api/users. Search matches name or email; Role filters by exact role.</summary>
public record UsersQuery : PageQuery, IValidatableObject
{
    public static readonly IReadOnlyList<string> SortFields = ["name", "email", "role", "createdAt"];

    public string? Role { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Role is not null && !Roles.All.Contains(Role))
            yield return new ValidationResult(
                $"Role must be one of: {string.Join(", ", Roles.All)}.", [nameof(Role)]);

        if (Sort is not null && !SortFields.Contains(Sort.TrimStart('-')))
            yield return new ValidationResult(
                $"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).", [nameof(Sort)]);
    }
}
