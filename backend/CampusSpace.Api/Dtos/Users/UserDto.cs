using CampusSpace.Api.Models;

namespace CampusSpace.Api.Dtos.Users;

/// <summary>A user as clients see it. Never includes the password hash.</summary>
public record UserDto(long Id, string FullName, string Email, string Role, bool IsActive, DateTime CreatedAt)
{
    public static UserDto FromEntity(User user) =>
        new(user.Id, user.FullName, user.Email, user.Role, user.IsActive, user.CreatedAt);
}
