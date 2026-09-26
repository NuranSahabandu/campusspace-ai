using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Dtos.Auth;

/// <summary>Self-registration. There is deliberately no Role: every new account is a Student.</summary>
public record RegisterRequest(
    [Required, MaxLength(100)] string FullName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password);
