using System.Text.Json;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Dtos.Quotations;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class QuotationService(
    AppDbContext db,
    ICurrentUser currentUser,
    IQuotationCalculator calculator,
    IBookingRequestService requests) : IQuotationService
{
    public const string OwnRoleMessage = "You can only preview prices for your own role.";
    public const string RoleRequiredMessage = "Choose the requester role to price for.";

    public async Task<QuotationDto?> PreviewAsync(QuotePreviewRequest request, CancellationToken ct = default)
    {
        string role;
        if (currentUser.IsInRole(Roles.FacilitiesOfficer))
            role = request.RequesterRole ?? throw new BusinessRuleException(nameof(request.RequesterRole), RoleRequiredMessage);
        else
        {
            role = currentUser.IsInRole(Roles.Lecturer) ? RequesterRoles.Lecturer : RequesterRoles.Student;
            if (request.RequesterRole is { } asked && asked != role)
                throw new BusinessRuleException(nameof(request.RequesterRole), OwnRoleMessage);
        }

        var equipment = (request.Equipment ?? []).Select(l => new QuoteEquipmentLine(l.TypeId, l.Quantity)).ToList();
        var quote = await calculator.CalculateAsync(
            new QuoteInput(request.RoomId!.Value, request.Start!.Value, request.End!.Value, role, equipment), ct);
        return quote is null ? null : new QuotationDto(null, null, null,
            quote.Lines.Select(l => new QuotationLineDto(l.Kind, l.Description, l.Qty, l.UnitPrice, l.LineTotal)).ToList(),
            quote.Subtotal, quote.Discount, quote.DiscountReason, quote.Exempt, quote.Total);
    }

    public async Task<QuotationDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var requestId = await db.Quotations.Where(q => q.Id == id).Select(q => (long?)q.RequestId).SingleOrDefaultAsync(ct);
        // The same rule as request detail: the owner or an Officer.
        return requestId is { } rid && await requests.EnsureCanReadAsync(rid, ct)
            ? await LoadAsync(db.Quotations.Where(q => q.Id == id), ct)
            : null;
    }

    public async Task<QuotationDto?> GetForRequestAsync(long requestId, CancellationToken ct = default) =>
        await requests.EnsureCanReadAsync(requestId, ct)
            ? await LoadAsync(db.Quotations.Where(q => q.RequestId == requestId && QuotationStatuses.Live.Contains(q.Status)), ct)
            : null;

    private static Task<QuotationDto?> LoadAsync(IQueryable<Quotation> quotations, CancellationToken ct) =>
        quotations.AsNoTracking().Select(q => new QuotationDto(
            q.Id, q.RequestId, q.Status,
            q.Lines.OrderBy(l => l.Id).Select(l => new QuotationLineDto(l.Kind, l.Description, l.Qty, l.UnitPrice, l.LineTotal)).ToList(),
            q.Subtotal, q.Discount, q.DiscountReason, q.IsExempt, q.Total, QuotationDto.Lkr))
            .SingleOrDefaultAsync(ct);

    /// <summary>
    /// Marks the request's live (Draft or Issued) quotes Void, for a cancelled request. The rows are tracked, so the
    /// caller's SaveChanges saves and audits them with the rest of its change; nothing is saved here. Static because
    /// BookingRequestService calls it, and this service already depends on IBookingRequestService.
    /// </summary>
    public static async Task VoidLiveAsync(AppDbContext db, long requestId, CancellationToken ct = default)
    {
        var live = await db.Quotations
            .Where(q => q.RequestId == requestId && QuotationStatuses.Live.Contains(q.Status))
            .ToListAsync(ct);
        foreach (var quotation in live)
            quotation.Status = QuotationStatuses.Void;
    }

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
        await VoidByUpdateAsync(live.Select(q => q.Id).ToList(), ct);

        var quotation = NewQuotation(requestId, quote, QuotationStatuses.Draft);
        db.Quotations.Add(quotation);
        return quotation;
    }

    public async Task<IssuedQuote> IssueForApprovalAsync(long requestId, Guid runId, QuoteResult quote, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("IssueForApprovalAsync must run inside the approval transaction.");

        var live = await db.Quotations.AsNoTracking().Include(q => q.Lines)
            .Where(q => q.RequestId == requestId && QuotationStatuses.Live.Contains(q.Status))
            .ToListAsync(ct);
        if (live.Any(q => q.Status == QuotationStatuses.Issued))
            throw new ConflictException(QuotationConfiguration.LiveQuoteMessage);

        var draft = live.SingleOrDefault();
        if (draft is not null && draft.AgentRunId == runId && Matches(draft, quote))
        {
            var tracked = await db.Quotations.SingleAsync(q => q.Id == draft.Id, ct);
            tracked.Status = QuotationStatuses.Issued;
            return new IssuedQuote(tracked, draft.Total, Recalculated: false);
        }

        // Prices changed since the Draft (a new pricing rule or fee), or there is no Draft of this run: the Draft is voided
        // and the price computed now is issued. Voided first, for the same filtered-index reason as in CreateDraftAsync.
        await VoidByUpdateAsync(live.Select(q => q.Id).ToList(), ct);
        var issued = NewQuotation(requestId, quote, QuotationStatuses.Issued);
        issued.AgentRunId = runId;
        db.Quotations.Add(issued);
        return new IssuedQuote(issued, draft?.Total, Recalculated: true);
    }

    /// <summary>Same totals, exemption and lines (in order) as the calculator's result.</summary>
    private static bool Matches(Quotation draft, QuoteResult quote) =>
        draft.Subtotal == quote.Subtotal && draft.Discount == quote.Discount && draft.Total == quote.Total
        && draft.IsExempt == quote.Exempt && draft.DiscountReason == quote.DiscountReason
        && draft.Lines.OrderBy(l => l.Id)
            .Select(l => (l.Kind, l.EquipmentTypeId, l.Description, l.Qty, l.UnitPrice, l.LineTotal))
            .SequenceEqual(quote.Lines.Select(l => (l.Kind, l.EquipmentTypeId, l.Description, l.Qty, l.UnitPrice, l.LineTotal)));

    /// <summary>
    /// Voids quotes with ExecuteUpdate, so the UPDATE reaches the database before a later INSERT (EF can't order an UPDATE
    /// before an INSERT for a filtered index). ExecuteUpdate bypasses the automatic audit, so the same audit row SaveChanges
    /// would write is added by hand.
    /// </summary>
    private async Task VoidByUpdateAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return;
        var now = DateTime.UtcNow;
        await db.Quotations
            .Where(q => ids.Contains(q.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QuotationStatuses.Void).SetProperty(q => q.UpdatedAt, now), ct);
        var details = JsonSerializer.Serialize(new { changed = new[] { nameof(Quotation.Status) } });
        db.AuditLogs.AddRange(ids.Select(id => new AuditLog
        {
            UserId = currentUser.UserId,
            Action = AuditActions.Updated,
            EntityType = nameof(Quotation),
            EntityId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DetailsJson = details,
            At = now,
        }));
    }

    private static Quotation NewQuotation(long requestId, QuoteResult quote, string status) => new()
    {
        RequestId = requestId,
        Subtotal = quote.Subtotal,
        Discount = quote.Discount,
        Total = quote.Total,
        DiscountReason = quote.DiscountReason,
        IsExempt = quote.Exempt,
        Status = status,
        // Unit prices are copied: a later fee or rule change doesn't change this quote.
        Lines = quote.Lines.Select(l => new QuotationLine
        {
            Kind = l.Kind, EquipmentTypeId = l.EquipmentTypeId, Description = l.Description,
            Qty = l.Qty, UnitPrice = l.UnitPrice, LineTotal = l.LineTotal,
        }).ToList(),
    };
}
