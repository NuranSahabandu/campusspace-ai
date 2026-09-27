using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class QuotationLineConfiguration : IEntityTypeConfiguration<QuotationLine>
{
    public const int DescriptionMaxLength = 200;

    public void Configure(EntityTypeBuilder<QuotationLine> builder)
    {
        var kinds = string.Join(", ", QuotationLineKinds.All.Select(k => $"'{k}'"));
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_QuotationLines_Kind", $"\"Kind\" IN ({kinds})");
            // Equipment lines name their type; the room line doesn't.
            t.HasCheckConstraint("CK_QuotationLines_Kind_Type",
                $"(\"Kind\" = '{QuotationLineKinds.Room}') = (\"EquipmentTypeId\" IS NULL)");
            t.HasCheckConstraint("CK_QuotationLines_Qty", "\"Qty\" > 0");
            t.HasCheckConstraint("CK_QuotationLines_UnitPrice", "\"UnitPrice\" >= 0");
            // Postgres round(numeric) rounds half away from zero, like IQuotationCalculator.
            t.HasCheckConstraint("CK_QuotationLines_LineTotal", "\"LineTotal\" = round(\"Qty\" * \"UnitPrice\", 2)");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Kind).IsRequired().HasMaxLength(20);
        builder.Property(l => l.Description).IsRequired().HasMaxLength(DescriptionMaxLength);
        builder.Property(l => l.Qty).HasColumnType("numeric(10,2)");
        builder.Property(l => l.UnitPrice).HasColumnType("numeric(10,2)");
        builder.Property(l => l.LineTotal).HasColumnType("numeric(10,2)");

        // A line means nothing without its quote; a type on a quote can't be deleted (409 "In use").
        builder.HasOne(l => l.Quotation).WithMany(q => q.Lines).HasForeignKey(l => l.QuotationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.EquipmentType).WithMany().HasForeignKey(l => l.EquipmentTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.QuotationId);
        builder.HasIndex(l => l.EquipmentTypeId);
    }
}
