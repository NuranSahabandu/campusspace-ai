namespace CampusSpace.Api.Auth;

/// <summary>
/// The caller of the current HTTP request, read from the JWT claims. Every member is null (or false)
/// outside a request or for an anonymous caller, for example during startup seeding.
/// </summary>
public interface ICurrentUser
{
    long? UserId { get; }
    string? Role { get; }
    bool IsInRole(string role);
}
