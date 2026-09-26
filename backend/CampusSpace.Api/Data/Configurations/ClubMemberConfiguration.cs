using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class ClubMemberConfiguration : IEntityTypeConfiguration<ClubMember>
{
    /// <summary>At most one representative per club. GlobalExceptionHandler maps its 23505 to a specific 409.</summary>
    public const string OneRepresentativeIndex = "IX_ClubMembers_OneRepresentative";

    public void Configure(EntityTypeBuilder<ClubMember> builder)
    {
        builder.HasKey(m => new { m.ClubId, m.UserId });

        builder.Property(m => m.JoinedAt).IsRequired();

        builder.HasOne(m => m.Club).WithMany(c => c.Members).HasForeignKey(m => m.ClubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);

        // ClubId is the leading PK column, so only UserId needs its own FK index.
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.ClubId, OneRepresentativeIndex).IsUnique().HasFilter("\"IsRepresentative\"");
    }
}
