using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampusSpace.Api.Data.Configurations;

public class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    /// <summary>At most one Draft or Issued quote per request. GlobalExceptionHandler maps a violation to a 409.</summary>
    public const string LiveQuoteIndex = "IX_Quotations_RequestId_Live";
    public const string LiveQuoteMessage = "Request already has a live quotation";
    public const int DiscountReasonMaxLength = 200;

    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        var statuses = string.Join(", ", QuotationStatuses.All.Select(s => $"'{s}'"));
        var live = string.Join(", ", QuotationStatuses.Live.Select(s => $"'{s}'"));
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Quotations_Subtotal", "\"Subtotal\" >= 0");
            t.HasCheckConstraint("CK_Quotations_Discount", "\"Discount\" >= 0");
            t.HasCheckConstraint("CK_Quotations_Total", "\"Total\" >= 0");
            t.HasCheckConstraint("CK_Quotations_Discount_LE_Subtotal", "\"Discount\" <= \"Subtotal\"");
            t.HasCheckConstraint("CK_Quotations_Total_Equals_Subtotal_Minus_Discount", "\"Total\" = \"Subtotal\" - \"Discount\"");
            // The lecturer exemption discounts the whole quote and says why.
            t.HasCheckConstraint("CK_Quotations_Exempt",
                "NOT \"IsExempt\" OR (\"Discount\" = \"Subtotal\" AND \"DiscountReason\" IS NOT NULL)");
            t.HasCheckConstraint("CK_Quotations_Status", $"\"Status\" IN ({statuses})");
        });

        builder.HasKey(q => q.Id);

        builder.Property(q => q.Subtotal).HasColumnType("numeric(10,2)");
        builder.Property(q => q.Discount).HasColumnType("numeric(10,2)");
        builder.Property(q => q.Total).HasColumnType("numeric(10,2)");
        builder.Property(q => q.DiscountReason).HasMaxLength(DiscountReasonMaxLength);
        builder.Property(q => q.Status).IsRequired().HasMaxLength(20);

        builder.HasOne(q => q.Request).WithMany().HasForeignKey(q => q.RequestId).OnDelete(DeleteBehavior.Restrict);

        // The FK index (every quote of a request, Void ones included), and the partial unique index for live quotes.
        builder.HasIndex(q => q.RequestId);
        builder.HasIndex(q => q.RequestId, LiveQuoteIndex).IsUnique().HasFilter($"\"Status\" IN ({live})");
    }
}
