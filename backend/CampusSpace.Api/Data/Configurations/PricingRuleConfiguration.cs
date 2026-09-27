using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
{
    /// <summary>One rule per room type, role and start date. GlobalExceptionHandler maps a violation to a 409.</summary>
    public const string UniqueRuleIndex = "IX_PricingRules_RoomType_RequesterRole_ValidFrom";
    public const string DuplicateRuleMessage = "A rule for this room type, role and date already exists";

    public void Configure(EntityTypeBuilder<PricingRule> builder)
    {
        var roomTypes = string.Join(", ", RoomTypes.All.Select(t => $"'{t}'"));
        var roles = string.Join(", ", RequesterRoles.All.Select(r => $"'{r}'"));
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PricingRules_RoomType", $"\"RoomType\" IN ({roomTypes})");
            t.HasCheckConstraint("CK_PricingRules_RequesterRole", $"\"RequesterRole\" IN ({roles})");
            t.HasCheckConstraint("CK_PricingRules_HourlyRate", "\"HourlyRate\" >= 0");
            t.HasCheckConstraint("CK_PricingRules_Exempt_ZeroRate", "NOT \"IsExempt\" OR \"HourlyRate\" = 0");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.RoomType).IsRequired().HasMaxLength(30);
        builder.Property(r => r.RequesterRole).IsRequired().HasMaxLength(30);
        builder.Property(r => r.HourlyRate).HasColumnType("numeric(10,2)");
        builder.Property(r => r.ValidFrom).HasColumnType("date");

        // Also serves the effective-rule lookup: equality on the first two columns, then the latest ValidFrom.
        builder.HasIndex(r => new { r.RoomType, r.RequesterRole, r.ValidFrom }, UniqueRuleIndex).IsUnique();
    }
}
