using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Users;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class UserService(AppDbContext db, ICurrentUser currentUser) : IUserService
{
    public Task<PagedResult<UserDto>> ListAsync(UsersQuery query, CancellationToken ct = default)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = query.Search.ToContainsPattern();
            users = users.Where(u => EF.Functions.ILike(u.FullName, pattern) || EF.Functions.ILike(u.Email, pattern));
        }

        if (query.Role is not null)
            users = users.Where(u => u.Role == query.Role);

        // Id is the tiebreak, so pages never overlap or skip rows.
        users = query.Sort switch
        {
            "name" => users.OrderBy(u => u.FullName).ThenBy(u => u.Id),
            "-name" => users.OrderByDescending(u => u.FullName).ThenBy(u => u.Id),
            "email" => users.OrderBy(u => u.Email).ThenBy(u => u.Id),
            "-email" => users.OrderByDescending(u => u.Email).ThenBy(u => u.Id),
            "role" => users.OrderBy(u => u.Role).ThenBy(u => u.Id),
            "-role" => users.OrderByDescending(u => u.Role).ThenBy(u => u.Id),
            "createdAt" => users.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id),
            "-createdAt" => users.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id),
            _ => users.OrderBy(u => u.Id),
        };

        return users
            .Select(u => new UserDto(u.Id, u.FullName, u.Email, u.Role, u.IsActive, u.CreatedAt))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<UserDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? null : UserDto.FromEntity(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var email = AuthService.NormalizeEmail(request.Email);
        // Friendly check first. The unique index still catches a concurrent insert (23505 -> 409).
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("Email is already registered");

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return UserDto.FromEntity(user);
    }

    public async Task<UserDto?> UpdateAsync(long id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return null;

        // Lock-out guard: an Admin editing their own account keeps it active and keeps the Admin role.
        if (id == currentUser.UserId)
        {
            if (request.IsActive == false)
                throw new BusinessRuleException(nameof(request.IsActive), "You cannot deactivate your own account.");
            if (request.Role != user.Role)
                throw new BusinessRuleException(nameof(request.Role), "You cannot change your own role.");
        }

        user.FullName = request.FullName.Trim();
        user.Role = request.Role;
        user.IsActive = request.IsActive!.Value;
        await db.SaveChangesAsync(ct);
        return UserDto.FromEntity(user);
    }
}
