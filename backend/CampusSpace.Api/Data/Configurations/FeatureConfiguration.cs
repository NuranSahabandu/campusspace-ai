using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class FeatureConfiguration : IEntityTypeConfiguration<Feature>
{
    /// <summary>Lower-case snake_case. FeatureService checks it too, for a 400 instead of a 500.</summary>
    public const string CodePattern = "^[a-z][a-z0-9]*(_[a-z0-9]+)*$";
    public const int CodeMaxLength = 50;

    public void Configure(EntityTypeBuilder<Feature> builder)
    {
        builder.ToTable(t =>
        {
            // The agents match these codes exactly.
            t.HasCheckConstraint("CK_Features_Code_Format", $"\"Code\" ~ '{CodePattern}'");
            t.HasCheckConstraint("CK_Features_Name_NotBlank", "btrim(\"Name\") <> ''");
        });

        builder.HasKey(f => f.Id);

        // Unique through AK_Features_Code, the alternate key that EquipmentTypes.CoveredByFeatureCode references
        // (see EquipmentTypeConfiguration). A separate unique index would be redundant. Codes are immutable.
        builder.Property(f => f.Code).IsRequired().HasMaxLength(CodeMaxLength);
        builder.Property(f => f.Name).IsRequired().HasMaxLength(100);
    }
}
