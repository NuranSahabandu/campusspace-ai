using CampusSpace.Api.Dtos.Clubs;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Services;

/// <summary>
/// Clubs and their members. Methods return null when the club (or member) does not exist.
/// Rule breaks throw BusinessRuleException (400) or ConflictException (409).
/// </summary>
public interface IClubService
{
    Task<PagedResult<ClubDto>> ListAsync(ClubsQuery query, CancellationToken ct = default);

    /// <summary>Null if missing, or inactive and the caller is not an Admin.</summary>
    Task<ClubDetailDto?> GetAsync(long id, CancellationToken ct = default);

    Task<ClubDetailDto> CreateAsync(CreateClubRequest request, CancellationToken ct = default);
    Task<ClubDetailDto?> UpdateAsync(long id, UpdateClubRequest request, CancellationToken ct = default);

    /// <summary>Only active Students and Lecturers. IsRepresentative replaces the current representative.</summary>
    Task<ClubDetailDto?> AddMemberAsync(long clubId, AddClubMemberRequest request, CancellationToken ct = default);

    /// <summary>False if the user is not a member of the club.</summary>
    Task<bool> RemoveMemberAsync(long clubId, long userId, CancellationToken ct = default);

    /// <summary>The user must already be a member. The old representative is unset.</summary>
    Task<ClubDetailDto?> SetRepresentativeAsync(long clubId, SetRepresentativeRequest request, CancellationToken ct = default);
}
