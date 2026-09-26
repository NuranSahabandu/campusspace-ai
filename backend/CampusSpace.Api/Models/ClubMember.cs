namespace CampusSpace.Api.Models;

/// <summary>
/// A Student or Lecturer in a club. At most one member per club is the representative
/// (ClubService, backed by the IX_ClubMembers_OneRepresentative filtered unique index).
/// </summary>
public class ClubMember : IAuditable
{
    public long ClubId { get; set; }
    public Club Club { get; set; } = null!;
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public bool IsRepresentative { get; set; }
    public DateTime JoinedAt { get; set; }
}
