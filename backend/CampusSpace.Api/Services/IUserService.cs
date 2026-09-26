using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Users;

namespace CampusSpace.Api.Services;

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(UsersQuery query, CancellationToken ct = default);

    Task<UserDto?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>Throws ConflictException if the email is taken.</summary>
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);

    /// <summary>Null if the user does not exist. Throws BusinessRuleException if the caller would lock themselves out.</summary>
    Task<UserDto?> UpdateAsync(long id, UpdateUserRequest request, CancellationToken ct = default);
}
