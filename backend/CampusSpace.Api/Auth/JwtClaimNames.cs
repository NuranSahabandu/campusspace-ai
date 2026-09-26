namespace CampusSpace.Api.Auth;

/// <summary>
/// Claim names inside our JWTs. Inbound claim mapping is off (MapInboundClaims = false),
/// so these short names are exactly what the API sees on User.Claims.
/// </summary>
public static class JwtClaimNames
{
    public const string Sub = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string Jti = "jti";
}
