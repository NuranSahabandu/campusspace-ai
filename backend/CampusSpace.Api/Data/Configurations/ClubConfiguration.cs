using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_Clubs_Name_NotBlank", "btrim(\"Name\") <> ''"));

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(c => c.Name).IsUnique();

        // Sentinel true: EF omits the column only when the value is still true, so an explicit false is saved.
        builder.Property(c => c.IsActive).HasDefaultValue(true).HasSentinel(true);
    }
}
