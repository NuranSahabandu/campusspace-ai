using System.Security.Claims;
using System.Text;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Models;
using CampusSpace.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CampusSpace.Api.Services;

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var jwt = options.Value;
        // All three times come from the injected clock, so tests can mint already-expired tokens.
        var now = clock.GetUtcNow().UtcDateTime;
        var expiresAt = now.AddMinutes(jwt.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtClaimNames.Email, user.Email),
                new Claim(JwtClaimNames.Name, user.FullName),
                new Claim(JwtClaimNames.Role, user.Role),
                new Claim(JwtClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256),
        };

        return (_handler.CreateToken(descriptor), expiresAt);
    }
}
