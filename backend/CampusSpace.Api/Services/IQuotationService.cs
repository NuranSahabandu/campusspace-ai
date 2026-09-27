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
}
