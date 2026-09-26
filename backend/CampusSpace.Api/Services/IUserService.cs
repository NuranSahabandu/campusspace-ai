using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Users;

namespace CampusSpace.Api.Services;

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(UsersQuery query, CancellationToken ct = default);
}
