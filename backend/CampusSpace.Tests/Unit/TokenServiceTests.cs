using CampusSpace.Api.Auth;
using CampusSpace.Api.Models;
using CampusSpace.Api.Options;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CampusSpace.Tests.Unit;

public class TokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions Options = new()
    {
        Key = new string('k', 64),
        Issuer = "issuer",
        Audience = "audience",
        AccessTokenMinutes = 120,
    };

    private static readonly User User = new()
    {
        Id = 42,
        FullName = "Kavindi Perera",
        Email = "kavindi@campusspace.local",
        Role = Roles.Student,
    };

    private static (JsonWebToken Jwt, DateTime ExpiresAt) Create()
    {
        var service = new TokenService(Microsoft.Extensions.Options.Options.Create(Options), new FixedTimeProvider(Now));
        var (token, expiresAt) = service.CreateAccessToken(User);
        return (new JsonWebToken(token), expiresAt);
    }

    [Fact]
    public void Token_carries_sub_email_name_and_role_with_short_claim_names()
    {
        var (jwt, _) = Create();

        jwt.GetClaim(JwtClaimNames.Sub).Value.Should().Be("42");
        jwt.GetClaim(JwtClaimNames.Email).Value.Should().Be("kavindi@campusspace.local");
        jwt.GetClaim(JwtClaimNames.Name).Value.Should().Be("Kavindi Perera");
        jwt.GetClaim(JwtClaimNames.Role).Value.Should().Be(Roles.Student);
        jwt.GetClaim(JwtClaimNames.Jti).Value.Should().NotBeNullOrEmpty();
        jwt.Issuer.Should().Be("issuer");
        jwt.Audiences.Should().Equal("audience");
        jwt.Alg.Should().Be(SecurityAlgorithms.HmacSha256);
    }

    [Fact]
    public void Expiry_is_now_plus_AccessTokenMinutes_in_utc()
    {
        var (jwt, expiresAt) = Create();

        var expected = Now.UtcDateTime.AddMinutes(120);
        expiresAt.Should().Be(expected);
        expiresAt.Kind.Should().Be(DateTimeKind.Utc);
        jwt.ValidTo.Should().Be(expected);
        jwt.ValidFrom.Should().Be(Now.UtcDateTime);
    }
}
