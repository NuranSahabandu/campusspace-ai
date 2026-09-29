using CampusSpace.Api.Dtos.Quotations;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Services;

/// <summary>Quotations (§9 Component D): saving a calculated quote as a Draft, previews and reads.</summary>
public interface IQuotationService
{
    /// <summary>
    /// Voids the request's Draft (if any) and adds a new Draft with the quote's lines, for the Phase 3 poller. Must run
    /// inside the caller's transaction: the void runs at once, while the new Draft and the void's audit rows are only
    /// added to the context, so the caller's SaveChanges and commit make them atomic. An Issued quote is a 409.
    /// </summary>
    Task<Quotation> CreateDraftAsync(long requestId, QuoteResult quote, CancellationToken ct = default);

    /// <summary>
    /// For the approval transaction (must run inside it; nothing is saved here). Issues the request's Draft of run
    /// <paramref name="runId"/> when it matches <paramref name="quote"/> (the price recomputed at approval); otherwise voids
    /// the Draft and adds a new Issued quote with the recomputed price. Either way the Issued quote carries the run id. An
    /// Issued quote already present is a 409.
    /// </summary>
    Task<IssuedQuote> IssueForApprovalAsync(long requestId, Guid runId, QuoteResult quote, CancellationToken ct = default);

    /// <summary>
    /// Prices a slot for the caller without saving. Students and Lecturers are priced as their own role (a different
    /// RequesterRole is a 400); an Officer must name the role. Null when the room is unknown or inactive (404).
    /// </summary>
    Task<QuotationDto?> PreviewAsync(QuotePreviewRequest request, CancellationToken ct = default);

    /// <summary>Null if not found. Throws ForbiddenException for a requester who doesn't own the request.</summary>
    Task<QuotationDto?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// The request's live (Draft or Issued) quote. Null if the request doesn't exist or has none. Throws
    /// ForbiddenException for a requester who doesn't own the request.
    /// </summary>
    Task<QuotationDto?> GetForRequestAsync(long requestId, CancellationToken ct = default);
}

/// <summary>The issued quote, the Draft's total (null without a Draft), and whether the price was recomputed.</summary>
public sealed record IssuedQuote(Quotation Quotation, decimal? DraftTotal, bool Recalculated);
