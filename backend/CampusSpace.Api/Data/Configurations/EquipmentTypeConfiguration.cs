using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class EquipmentTypeConfiguration : IEntityTypeConfiguration<EquipmentType>
{
    /// <summary>Upper-case letters and digits in hyphen-separated parts (MIC-WIRELESS). EquipmentTypeService checks it too, for a 400.</summary>
    public const string CodePattern = "^[A-Z0-9]+(-[A-Z0-9]+)*$";
    public const int CodeMinLength = 2;
    public const int CodeMaxLength = 40;

    public void Configure(EntityTypeBuilder<EquipmentType> builder)
    {
        var allowedCategories = string.Join(", ", EquipmentCategories.All.Select(c => $"'{c}'"));
        builder.ToTable(t =>
        {
            // The agents match these codes exactly.
            t.HasCheckConstraint("CK_EquipmentTypes_Code_Format", $"\"Code\" ~ '{CodePattern}' AND char_length(\"Code\") >= {CodeMinLength}");
            t.HasCheckConstraint("CK_EquipmentTypes_Name_NotBlank", "btrim(\"Name\") <> ''");
            t.HasCheckConstraint("CK_EquipmentTypes_Category", $"\"Category\" IN ({allowedCategories})");
            t.HasCheckConstraint("CK_EquipmentTypes_FeePerBooking", "\"FeePerBooking\" >= 0");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).IsRequired().HasMaxLength(CodeMaxLength);
        builder.HasIndex(t => t.Code).IsUnique();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Category).IsRequired().HasMaxLength(20);
        builder.Property(t => t.FeePerBooking).HasColumnType("numeric(10,2)");

        // Addendum Change B: FK to the feature's code, not its id, because the agents work with codes.
        // This makes Features.Code an alternate key (AK_Features_Code), which is also its only uniqueness mechanism.
        // RESTRICT: a feature that an equipment type covers cannot be deleted (409 "In use").
        builder.Property(t => t.CoveredByFeatureCode).HasMaxLength(FeatureConfiguration.CodeMaxLength);
        builder.HasOne(t => t.CoveredByFeature).WithMany()
            .HasForeignKey(t => t.CoveredByFeatureCode)
            .HasPrincipalKey(f => f.Code)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.CoveredByFeatureCode);
    }
}
