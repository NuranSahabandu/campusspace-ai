using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Users;
using CampusSpace.Api.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class UserService(AppDbContext db) : IUserService
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
}
