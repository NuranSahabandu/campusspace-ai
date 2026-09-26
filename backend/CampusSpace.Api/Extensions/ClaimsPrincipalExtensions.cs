using System.Globalization;
using System.Security.Claims;
using CampusSpace.Api.Auth;

namespace CampusSpace.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The Users.Id from the token's "sub" claim. Only call this on authenticated requests.</summary>
    public static long GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtClaimNames.Sub);
        return long.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new InvalidOperationException("The authenticated user has no numeric 'sub' claim.");
    }
}
