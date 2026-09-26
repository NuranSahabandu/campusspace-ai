using System.Globalization;
using System.Security.Claims;

namespace CampusSpace.Api.Auth;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;

    public long? UserId =>
        long.TryParse(Principal?.FindFirstValue(JwtClaimNames.Sub), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public string? Role => Principal?.FindFirstValue(JwtClaimNames.Role);

    public bool IsInRole(string role) => Role == role;
}
