using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public const int AddressMaxLength = 256;
    public const int ErrorMaxLength = 500;
    public const int ProviderMessageIdMaxLength = 100;
    /// <summary>One send plus at most two retries (plan §14).</summary>
    public const int MaxAttempts = 3;

    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("NotificationLogs", t =>
        {
            t.HasCheckConstraint("CK_NotificationLogs_Kind", $"\"Kind\" IN ({AgentRunConfiguration.SqlList(NotificationKinds.All)})");
            t.HasCheckConstraint("CK_NotificationLogs_Channel",
                $"\"Channel\" IN ({AgentRunConfiguration.SqlList(NotificationChannels.All)})");
            t.HasCheckConstraint("CK_NotificationLogs_Status",
                $"\"Status\" IN ({AgentRunConfiguration.SqlList(NotificationStatuses.All)})");
            t.HasCheckConstraint("CK_NotificationLogs_Attempts", $"\"Attempts\" BETWEEN 0 AND {MaxAttempts}");
            t.HasCheckConstraint("CK_NotificationLogs_SentAt",
                $"(\"Status\" = '{NotificationStatuses.Sent}') = (\"SentAt\" IS NOT NULL)");
            t.HasCheckConstraint("CK_NotificationLogs_Error",
                $"\"Status\" <> '{NotificationStatuses.Failed}' OR \"Error\" IS NOT NULL");
        });

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).IsRequired().HasMaxLength(30);
        builder.Property(n => n.Channel).IsRequired().HasMaxLength(20);
        builder.Property(n => n.Recipient).IsRequired().HasMaxLength(AddressMaxLength);
        builder.Property(n => n.RedirectedTo).HasMaxLength(AddressMaxLength);
        builder.Property(n => n.Status).IsRequired().HasMaxLength(20);
        builder.Property(n => n.Error).HasMaxLength(ErrorMaxLength);
        builder.Property(n => n.ProviderMessageId).HasMaxLength(ProviderMessageIdMaxLength);

        builder.HasOne(n => n.Request).WithMany().HasForeignKey(n => n.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(n => n.StatusHistory).WithMany().HasForeignKey(n => n.StatusHistoryId).OnDelete(DeleteBehavior.Restrict);

        // The request's emails, newest first. Also serves as the RequestId FK index.
        builder.HasIndex(n => new { n.RequestId, n.CreatedAt });
        // One email per status change: enqueueing is idempotent. Also serves as the StatusHistoryId FK index.
        builder.HasIndex(n => n.StatusHistoryId).IsUnique();
        // The dispatcher's queue: due Pending rows.
        builder.HasIndex(n => n.NextAttemptAt).HasFilter($"\"Status\" = '{NotificationStatuses.Pending}'");
        // The stale-Sending sweep.
        builder.HasIndex(n => n.LastAttemptAt).HasFilter($"\"Status\" = '{NotificationStatuses.Sending}'");
    }
}
