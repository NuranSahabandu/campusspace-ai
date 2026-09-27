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
    public const int CancelReasonMaxLength = RequestStatusHistoryConfiguration.ReasonMaxLength;

    public void Configure(EntityTypeBuilder<BookingRequest> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BookingRequests_Purpose_NotBlank", "btrim(\"Purpose\") <> ''");
            t.HasCheckConstraint("CK_BookingRequests_Attendees", $"\"Attendees\" BETWEEN {MinAttendees} AND {MaxAttendees}");
            t.HasCheckConstraint("CK_BookingRequests_RequestedEnd_After_Start", "\"RequestedEnd\" > \"RequestedStart\"");
            t.HasCheckConstraint("CK_BookingRequests_BudgetLkr", "\"BudgetLkr\" >= 0");
            t.HasCheckConstraint("CK_BookingRequests_Status", $"\"Status\" IN ({RequestStatusSql.InList})");
            // An officer cancellation is never late. CancelledAt is set only on a cancelled request (not the other way
            // round: a status moved to Cancelled outside the cancel operation, as tests do, has no CancelledAt).
            t.HasCheckConstraint("CK_BookingRequests_Cancellation_NotLateAndOfficer",
                "NOT (\"IsLateCancellation\" AND \"CancelledByOfficer\")");
            t.HasCheckConstraint("CK_BookingRequests_CancelledAt_Status",
                $"\"CancelledAt\" IS NULL OR \"Status\" = '{RequestStatuses.Cancelled}'");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Purpose).IsRequired().HasMaxLength(PurposeMaxLength);
        builder.Property(r => r.Notes).HasMaxLength(NotesMaxLength);
        builder.Property(r => r.BudgetLkr).HasColumnType("numeric(10,2)");
        builder.Property(r => r.Status).IsRequired().HasMaxLength(20);
        builder.Property(r => r.IsLateCancellation).HasDefaultValue(false);
        builder.Property(r => r.CancelledByOfficer).HasDefaultValue(false);
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
