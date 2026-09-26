using CampusSpace.Api.Auth;

namespace CampusSpace.Tests.Infrastructure;

public sealed class FakeCurrentUser(long? userId, string? role = null) : ICurrentUser
{
    public long? UserId => userId;
    public string? Role => role;
    public bool IsInRole(string r) => role == r;
}
