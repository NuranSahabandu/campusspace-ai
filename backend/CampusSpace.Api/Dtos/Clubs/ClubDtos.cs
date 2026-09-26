using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Dtos.Clubs;

/// <summary>A club in a list. RepresentativeName is null when the club has no representative.</summary>
public record ClubDto(long Id, string Name, bool IsActive, int MemberCount, string? RepresentativeName);

/// <summary>A member as any signed-in user sees it: no email.</summary>
public record ClubMemberDto(long UserId, string FullName, string Role, bool IsRepresentative, DateTime JoinedAt);

/// <summary>A club with its members, representative first.</summary>
public record ClubDetailDto(
    long Id, string Name, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<ClubMemberDto> Members);

public record CreateClubRequest([Required, MaxLength(100)] string Name);

/// <summary>Deactivate with IsActive = false; clubs are never hard-deleted.</summary>
public record UpdateClubRequest([Required, MaxLength(100)] string Name, [Required] bool? IsActive);

public record AddClubMemberRequest([Range(1, long.MaxValue)] long UserId, bool IsRepresentative);

public record SetRepresentativeRequest([Range(1, long.MaxValue)] long UserId);
