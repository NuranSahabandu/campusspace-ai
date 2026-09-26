using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Clubs;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class ClubService(AppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IClubService
{
    /// <summary>The only roles that can join a club.</summary>
    public static readonly IReadOnlyList<string> MemberRoles = [Roles.Student, Roles.Lecturer];

    private bool IsAdmin => currentUser.IsInRole(Roles.Admin);

    public Task<PagedResult<ClubDto>> ListAsync(ClubsQuery query, CancellationToken ct = default)
    {
        var clubs = db.Clubs.AsNoTracking();

        if (!(query.IncludeInactive && IsAdmin))
            clubs = clubs.Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
            clubs = clubs.Where(c => EF.Functions.ILike(c.Name, query.Search.ToContainsPattern()));

        clubs = query.Sort switch
        {
            "-name" => clubs.OrderByDescending(c => c.Name).ThenBy(c => c.Id),
            "createdAt" => clubs.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            "-createdAt" => clubs.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id),
            _ => clubs.OrderBy(c => c.Name).ThenBy(c => c.Id),
        };

        return clubs
            .Select(c => new ClubDto(c.Id, c.Name, c.IsActive, c.Members.Count,
                c.Members.Where(m => m.IsRepresentative).Select(m => m.User.FullName).FirstOrDefault()))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<ClubDetailDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var club = await db.Clubs.AsNoTracking()
            .Where(c => c.Id == id && (c.IsActive || IsAdmin))
            .Select(c => new
            {
                c.Id, c.Name, c.IsActive, c.CreatedAt, c.UpdatedAt,
                Members = c.Members
                    .OrderByDescending(m => m.IsRepresentative).ThenBy(m => m.User.FullName)
                    .Select(m => new ClubMemberDto(m.UserId, m.User.FullName, m.User.Role, m.IsRepresentative, m.JoinedAt))
                    .ToList(),
            })
            .SingleOrDefaultAsync(ct);

        return club is null
            ? null
            : new ClubDetailDto(club.Id, club.Name, club.IsActive, club.CreatedAt, club.UpdatedAt, club.Members);
    }

    public async Task<ClubDetailDto> CreateAsync(CreateClubRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, exceptId: null, ct);

        var club = new Club { Name = name };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(club.Id, ct))!;
    }

    public async Task<ClubDetailDto?> UpdateAsync(long id, UpdateClubRequest request, CancellationToken ct = default)
    {
        var club = await db.Clubs.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (club is null)
            return null;

        var name = request.Name.Trim();
        await EnsureNameIsFreeAsync(name, exceptId: id, ct);

        club.Name = name;
        club.IsActive = request.IsActive!.Value;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ClubDetailDto?> AddMemberAsync(long clubId, AddClubMemberRequest request, CancellationToken ct = default)
    {
        var club = await db.Clubs.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clubId, ct);
        if (club is null)
            return null;
        if (!club.IsActive)
            throw new BusinessRuleException("ClubId", "Members cannot be added to an inactive club.");

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user is null || !user.IsActive)
            throw new BusinessRuleException(nameof(request.UserId), "User does not exist or is inactive.");
        if (!MemberRoles.Contains(user.Role))
            throw new BusinessRuleException(nameof(request.UserId), "Only students and lecturers can be club members.");
        if (await db.ClubMembers.AnyAsync(m => m.ClubId == clubId && m.UserId == request.UserId, ct))
            throw new ConflictException("User is already a member of this club");

        var member = new ClubMember { ClubId = clubId, UserId = request.UserId, JoinedAt = clock.GetUtcNow().UtcDateTime };
        if (request.IsRepresentative)
        {
            await ReplaceRepresentativeAsync(clubId, member, ct);
        }
        else
        {
            db.ClubMembers.Add(member);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(clubId, ct);
    }

    public async Task<bool> RemoveMemberAsync(long clubId, long userId, CancellationToken ct = default)
    {
        var member = await db.ClubMembers.SingleOrDefaultAsync(m => m.ClubId == clubId && m.UserId == userId, ct);
        if (member is null)
            return false;

        db.ClubMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ClubDetailDto?> SetRepresentativeAsync(long clubId, SetRepresentativeRequest request, CancellationToken ct = default)
    {
        if (!await db.Clubs.AnyAsync(c => c.Id == clubId, ct))
            return null;

        var member = await db.ClubMembers.SingleOrDefaultAsync(m => m.ClubId == clubId && m.UserId == request.UserId, ct)
            ?? throw new BusinessRuleException(nameof(request.UserId), "User is not a member of this club.");

        if (!member.IsRepresentative)
            await ReplaceRepresentativeAsync(clubId, member, ct);
        return await GetAsync(clubId, ct);
    }

    /// <summary>
    /// Unsets the current representative and saves, then makes <paramref name="newRepresentative"/> the representative
    /// (adding it if it is new) and saves, all in one transaction (an outer one is reused). Two saves because
    /// IX_ClubMembers_OneRepresentative is checked per statement and EF does not promise the order of UPDATEs in one save.
    /// </summary>
    private async Task ReplaceRepresentativeAsync(long clubId, ClubMember newRepresentative, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        var current = await db.ClubMembers.Where(m => m.ClubId == clubId && m.IsRepresentative).ToListAsync(ct);
        current.ForEach(m => m.IsRepresentative = false);
        await db.SaveChangesAsync(ct);

        newRepresentative.IsRepresentative = true;
        if (db.Entry(newRepresentative).State == EntityState.Detached)
            db.ClubMembers.Add(newRepresentative);
        await db.SaveChangesAsync(ct);

        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }

    /// <summary>Case-insensitive, so "robotics club" cannot sit next to "Robotics Club". The unique index still catches races (409).</summary>
    private async Task EnsureNameIsFreeAsync(string name, long? exceptId, CancellationToken ct)
    {
        var lower = name.ToLower();
        if (await db.Clubs.AnyAsync(c => c.Name.ToLower() == lower && c.Id != exceptId, ct))
            throw new ConflictException("Club name is already taken");
    }
}
