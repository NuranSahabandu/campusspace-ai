using CampusSpace.Api.Dtos.Auth;
using CampusSpace.Api.Dtos.Users;

namespace CampusSpace.Api.Services;

public interface IAuthService
{
    /// <summary>Creates a Student account. Throws ConflictException if the email is taken.</summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>Null for an unknown email, an inactive user or a wrong password. Callers must not say which.</summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Null if the user no longer exists or is inactive.</summary>
    Task<UserDto?> GetCurrentUserAsync(long userId, CancellationToken ct = default);
}
