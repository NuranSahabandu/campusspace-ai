using System.Text.Json;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class QuotationService(AppDbContext db, ICurrentUser currentUser) : IQuotationService
{
    public async Task<Quotation> CreateDraftAsync(long requestId, QuoteResult quote, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("CreateDraftAsync must run inside the caller's transaction.");

        var live = await db.Quotations
            .Where(q => q.RequestId == requestId && QuotationStatuses.Live.Contains(q.Status))
            .Select(q => new { q.Id, q.Status })
            .ToListAsync(ct);
        if (live.Any(q => q.Status == QuotationStatuses.Issued))
            throw new ConflictException(QuotationConfiguration.LiveQuoteMessage);

        // The void must reach the database before the insert, or IX_Quotations_RequestId_Live rejects the new Draft.
        // EF can't order an UPDATE before an INSERT for a filtered index, so it runs now (in the caller's transaction).
        // ExecuteUpdate bypasses the automatic audit, so the same audit row SaveChanges would write is added by hand.
        var drafts = live.Select(q => q.Id).ToList();
        if (drafts.Count > 0)
        {
            var now = DateTime.UtcNow;
            await db.Quotations
                .Where(q => drafts.Contains(q.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QuotationStatuses.Void).SetProperty(q => q.UpdatedAt, now), ct);
            var details = JsonSerializer.Serialize(new { changed = new[] { nameof(Quotation.Status) } });
            db.AuditLogs.AddRange(drafts.Select(id => new AuditLog
            {
                UserId = currentUser.UserId,
                Action = AuditActions.Updated,
                EntityType = nameof(Quotation),
                EntityId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DetailsJson = details,
                At = now,
            }));
        }

        var quotation = new Quotation
        {
            RequestId = requestId,
            Subtotal = quote.Subtotal,
            Discount = quote.Discount,
            Total = quote.Total,
            DiscountReason = quote.DiscountReason,
            IsExempt = quote.Exempt,
            Status = QuotationStatuses.Draft,
            // Unit prices are copied: a later fee or rule change doesn't change this quote.
            Lines = quote.Lines.Select(l => new QuotationLine
            {
                Kind = l.Kind, EquipmentTypeId = l.EquipmentTypeId, Description = l.Description,
                Qty = l.Qty, UnitPrice = l.UnitPrice, LineTotal = l.LineTotal,
            }).ToList(),
        };
        db.Quotations.Add(quotation);
        return quotation;
    }
}
