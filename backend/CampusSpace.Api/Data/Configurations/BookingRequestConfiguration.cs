using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class BookingRequestConfiguration : IEntityTypeConfiguration<BookingRequest>
{
    public const int MinAttendees = 1;
    public const int MaxAttendees = 2000;
    public const int PurposeMaxLength = 200;
    public const int NotesMaxLength = 1000;

    public void Configure(EntityTypeBuilder<BookingRequest> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BookingRequests_Purpose_NotBlank", "btrim(\"Purpose\") <> ''");
            t.HasCheckConstraint("CK_BookingRequests_Attendees", $"\"Attendees\" BETWEEN {MinAttendees} AND {MaxAttendees}");
            t.HasCheckConstraint("CK_BookingRequests_RequestedEnd_After_Start", "\"RequestedEnd\" > \"RequestedStart\"");
            t.HasCheckConstraint("CK_BookingRequests_BudgetLkr", "\"BudgetLkr\" >= 0");
            t.HasCheckConstraint("CK_BookingRequests_Status", $"\"Status\" IN ({RequestStatusSql.InList})");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Purpose).IsRequired().HasMaxLength(PurposeMaxLength);
        builder.Property(r => r.Notes).HasMaxLength(NotesMaxLength);
        builder.Property(r => r.BudgetLkr).HasColumnType("numeric(10,2)");
        builder.Property(r => r.Status).IsRequired().HasMaxLength(20);
        builder.Property(r => r.RequiredFeatures).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");

        builder.HasOne(r => r.Requester).WithMany().HasForeignKey(r => r.RequesterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Club).WithMany().HasForeignKey(r => r.ClubId).OnDelete(DeleteBehavior.Restrict);

        // The officer queue (status, oldest first) and the requester's open-request count (V11).
        // The second also serves as the RequesterId FK index.
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => new { r.RequesterId, r.Status });
        builder.HasIndex(r => r.ClubId);
    }
}

/// <summary>RequestStatuses.All as a SQL IN list, shared by the status CHECK constraints.</summary>
internal static class RequestStatusSql
{
    public static string InList => string.Join(", ", RequestStatuses.All.Select(s => $"'{s}'"));
}
