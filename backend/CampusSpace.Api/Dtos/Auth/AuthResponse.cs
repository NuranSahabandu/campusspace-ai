using CampusSpace.Api.Dtos.Users;

namespace CampusSpace.Api.Dtos.Auth;

/// <summary>ExpiresAt is UTC. Clients send AccessToken as "Authorization: Bearer ...".</summary>
public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
