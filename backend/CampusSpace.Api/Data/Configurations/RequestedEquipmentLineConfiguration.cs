using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class RequestedEquipmentLineConfiguration : IEntityTypeConfiguration<RequestedEquipmentLine>
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 50;

    public void Configure(EntityTypeBuilder<RequestedEquipmentLine> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_RequestedEquipmentLines_Quantity", $"\"Quantity\" BETWEEN {MinQuantity} AND {MaxQuantity}"));

        builder.HasKey(l => new { l.RequestId, l.TypeId });

        // CASCADE: a line means nothing without its request. RESTRICT: a requested type can't be deleted (409 "In use").
        builder.HasOne(l => l.Request).WithMany(r => r.EquipmentLines).HasForeignKey(l => l.RequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.Type).WithMany().HasForeignKey(l => l.TypeId).OnDelete(DeleteBehavior.Restrict);

        // RequestId is the leading PK column, so only TypeId needs its own FK index.
        builder.HasIndex(l => l.TypeId);
    }
}
