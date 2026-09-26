using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Dtos.Auth;

public record LoginRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(100)] string Password);
