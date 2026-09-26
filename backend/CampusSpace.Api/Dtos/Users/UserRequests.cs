using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Users;

/// <summary>Admin creates an account with any role and an initial password.</summary>
public record CreateUserRequest(
    [Required, MaxLength(100)] string FullName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password,
    [Required, ValidRole] string Role);

/// <summary>Email and password are not changed here. An Admin cannot deactivate themselves or change their own role.</summary>
public record UpdateUserRequest(
    [Required, MaxLength(100)] string FullName,
    [Required, ValidRole] string Role,
    [Required] bool? IsActive);
