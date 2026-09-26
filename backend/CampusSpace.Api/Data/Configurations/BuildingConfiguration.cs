using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Buildings_Code_Format", "\"Code\" ~ '^[A-Z0-9][A-Z0-9-]*$'");
            t.HasCheckConstraint("CK_Buildings_Name_NotBlank", "btrim(\"Name\") <> ''");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Code).IsRequired().HasMaxLength(10);
        builder.HasIndex(b => b.Code).IsUnique();
        builder.Property(b => b.Name).IsRequired().HasMaxLength(100);

        // Sentinel true: EF omits the column only when the value is still true, so an explicit false is saved.
        builder.Property(b => b.IsActive).HasDefaultValue(true).HasSentinel(true);
    }
}
