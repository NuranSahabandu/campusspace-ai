using CampusSpace.Api.Models;

namespace CampusSpace.Api.Services;

public interface ITokenService
{
    /// <summary>Issues a signed HS256 access token for the user. ExpiresAt is UTC.</summary>
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user);
}
