using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Auth;
using CampusSpace.Api.Dtos.Users;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class AuthService(AppDbContext db, ITokenService tokens, IAuditService audit) : IAuthService
{
    // Verified when the email is unknown, so an unknown email takes as long as a wrong password.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("timing-equaliser-not-a-password");

    /// <summary>Every email is stored and looked up in this form.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        // Friendly check first. The unique index still catches a concurrent insert (23505 -> 409).
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("Email is already registered");

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = Roles.Student,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return CreateResponse(user);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyHash);
        if (user is null || !passwordOk || !user.IsActive)
        {
            // Attributed to the matched account (if any) so an admin can spot attacks on it. Never the password.
            await audit.LogForUserAsync(user?.Id, AuditActions.LoginFailed, nameof(User), user?.Id.ToString(),
                new { email }, ct);
            return null;
        }

        await audit.LogForUserAsync(user.Id, AuditActions.Login, nameof(User), user.Id.ToString(), ct: ct);
        return CreateResponse(user);
    }

    public async Task<UserDto?> GetCurrentUserAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        return user is null ? null : UserDto.FromEntity(user);
    }

    private AuthResponse CreateResponse(User user)
    {
        var (token, expiresAt) = tokens.CreateAccessToken(user);
        return new AuthResponse(token, expiresAt, UserDto.FromEntity(user));
    }
}
