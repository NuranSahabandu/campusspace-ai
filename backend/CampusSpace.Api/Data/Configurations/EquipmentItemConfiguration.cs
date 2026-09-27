using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class EquipmentItemConfiguration : IEntityTypeConfiguration<EquipmentItem>
{
    public void Configure(EntityTypeBuilder<EquipmentItem> builder)
    {
        var allowedConditions = string.Join(", ", EquipmentConditions.All.Select(c => $"'{c}'"));
        var allowedStatuses = string.Join(", ", EquipmentItemStatuses.All.Select(s => $"'{s}'"));
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_EquipmentItems_AssetTag_NotBlank", "btrim(\"AssetTag\") <> ''");
            t.HasCheckConstraint("CK_EquipmentItems_Condition", $"\"Condition\" IN ({allowedConditions})");
            t.HasCheckConstraint("CK_EquipmentItems_Status", $"\"Status\" IN ({allowedStatuses})");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.AssetTag).IsRequired().HasMaxLength(30);
        builder.HasIndex(i => i.AssetTag).IsUnique();
        builder.Property(i => i.Condition).IsRequired().HasMaxLength(20);
        builder.Property(i => i.Status).IsRequired().HasMaxLength(20);
        builder.Property(i => i.Notes).HasMaxLength(500);

        // RESTRICT: a type that still has items cannot be deleted (409 "In use").
        builder.HasOne(i => i.Type).WithMany(t => t.Items).HasForeignKey(i => i.TypeId).OnDelete(DeleteBehavior.Restrict);
        // TypeId leads this index, so it also serves as the FK index. Availability will count items per type and status.
        builder.HasIndex(i => new { i.TypeId, i.Status });
    }
}
