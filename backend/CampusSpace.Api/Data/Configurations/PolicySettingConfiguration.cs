using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class PolicySettingConfiguration : IEntityTypeConfiguration<PolicySetting>
{
    public void Configure(EntityTypeBuilder<PolicySetting> builder)
    {
        var keys = string.Join(", ", PolicyKeys.All.Select(k => $"'{k}'"));
        var types = string.Join(", ", PolicyValueTypes.All.Select(t => $"'{t}'"));
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PolicySettings_Key", $"\"Key\" IN ({keys})");
            t.HasCheckConstraint("CK_PolicySettings_ValueType", $"\"ValueType\" IN ({types})");
        });

        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasColumnType("text");
        builder.Property(s => s.Value).IsRequired().HasColumnType("text");
        builder.Property(s => s.ValueType).IsRequired().HasColumnType("text");
        builder.Property(s => s.Description).IsRequired().HasColumnType("text");

        builder.HasOne(s => s.UpdatedBy).WithMany().HasForeignKey(s => s.UpdatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.UpdatedById);
    }
}
