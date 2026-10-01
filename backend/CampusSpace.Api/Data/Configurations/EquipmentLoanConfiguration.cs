using CampusSpace.Api.Models;
using CampusSpace.Api.Photos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class EquipmentLoanConfiguration : IEntityTypeConfiguration<EquipmentLoan>
{
    /// <summary>An item can be on only one open loan. A 23505 on it maps to 409 <see cref="ItemOnLoanMessage"/>.</summary>
    public const string OneOpenLoanIndex = "IX_EquipmentLoans_ItemId_Open";
    public const string ItemOnLoanMessage = "Item is already on loan";
    public const int DamageNoteMaxLength = 1000;

    public void Configure(EntityTypeBuilder<EquipmentLoan> builder)
    {
        var conditions = string.Join(", ", EquipmentConditions.All.Select(c => $"'{c}'"));
        builder.ToTable(t =>
        {
            // Open: nothing from check-in is set. Closed: who, when and the condition are all set.
            t.HasCheckConstraint("CK_EquipmentLoans_CheckIn",
                "(\"CheckedInAt\" IS NULL AND \"CheckedInById\" IS NULL AND \"ReturnCondition\" IS NULL " +
                "AND \"DamageNote\" IS NULL AND \"DamagePhotoKey\" IS NULL AND NOT \"IsLateReturn\") " +
                "OR (\"CheckedInAt\" IS NOT NULL AND \"CheckedInById\" IS NOT NULL AND \"ReturnCondition\" IS NOT NULL)");
            t.HasCheckConstraint("CK_EquipmentLoans_ReturnCondition", $"\"ReturnCondition\" IN ({conditions})");
            t.HasCheckConstraint("CK_EquipmentLoans_Damaged",
                $"\"ReturnCondition\" IS DISTINCT FROM '{EquipmentConditions.Damaged}' " +
                "OR (\"DamageNote\" IS NOT NULL AND \"DamagePhotoKey\" IS NOT NULL)");
            t.HasCheckConstraint("CK_EquipmentLoans_CheckedInAt", "\"CheckedInAt\" >= \"CheckedOutAt\"");
            t.HasCheckConstraint("CK_EquipmentLoans_DueAt", "\"DueAt\" > \"CheckedOutAt\"");
            // A random object key only (PhotoKeys), so a stored key can never name another object or leave the photo folder.
            t.HasCheckConstraint("CK_EquipmentLoans_DamagePhotoKey", "\"DamagePhotoKey\" ~ '^[0-9a-f]{32}\\.(jpg|png)$'");
            t.HasCheckConstraint("CK_EquipmentLoans_DamagePhotoContentType",
                $"\"DamagePhotoContentType\" IN ('{DamagePhotoRules.Jpeg.ContentType}', '{DamagePhotoRules.Png.ContentType}')");
            t.HasCheckConstraint("CK_EquipmentLoans_DamagePhotoSizeBytes",
                $"\"DamagePhotoSizeBytes\" BETWEEN 1 AND {DamagePhotoRules.MaxBytes}");
            // Key and content type come together; a size needs a key (it is null only on a legacy photo until imported).
            t.HasCheckConstraint("CK_EquipmentLoans_DamagePhotoMeta",
                "(\"DamagePhotoKey\" IS NULL) = (\"DamagePhotoContentType\" IS NULL) " +
                "AND (\"DamagePhotoSizeBytes\" IS NULL OR \"DamagePhotoKey\" IS NOT NULL)");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.ReturnCondition).HasMaxLength(20);
        builder.Property(l => l.DamageNote).HasMaxLength(DamageNoteMaxLength);
        builder.Property(l => l.DamagePhotoKey).HasMaxLength(40);
        builder.Property(l => l.DamagePhotoContentType).HasMaxLength(20);
        builder.Property(l => l.IsLateReturn).HasDefaultValue(false);

        builder.HasOne(l => l.Booking).WithMany().HasForeignKey(l => l.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Item).WithMany().HasForeignKey(l => l.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.CheckedOutBy).WithMany().HasForeignKey(l => l.CheckedOutById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.CheckedInBy).WithMany().HasForeignKey(l => l.CheckedInById).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.BookingId);
        // The partial index below covers open loans only, so the FK needs its own index too.
        builder.HasIndex(l => l.ItemId);
        builder.HasIndex(l => l.ItemId, OneOpenLoanIndex).IsUnique().HasFilter("\"CheckedInAt\" IS NULL");
        builder.HasIndex(l => l.CheckedOutById);
        builder.HasIndex(l => l.CheckedInById);
        // The overdue list: open loans by due time.
        builder.HasIndex(l => l.DueAt, "IX_EquipmentLoans_DueAt_Open").HasFilter("\"CheckedInAt\" IS NULL");
    }
}
