using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class EquipmentSubstituteConfiguration : IEntityTypeConfiguration<EquipmentSubstitute>
{
    public void Configure(EntityTypeBuilder<EquipmentSubstitute> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_EquipmentSubstitutes_NotSelf", "\"TypeId\" <> \"SubstituteTypeId\""));

        builder.HasKey(s => new { s.TypeId, s.SubstituteTypeId });

        // CASCADE on both sides: a substitute pair means nothing once either type is gone.
        builder.HasOne(s => s.Type).WithMany(t => t.Substitutes).HasForeignKey(s => s.TypeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.SubstituteType).WithMany().HasForeignKey(s => s.SubstituteTypeId).OnDelete(DeleteBehavior.Cascade);

        // TypeId is the leading PK column, so only SubstituteTypeId needs its own FK index.
        builder.HasIndex(s => s.SubstituteTypeId);
    }
}
