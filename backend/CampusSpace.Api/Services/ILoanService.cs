using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Loans;
using CampusSpace.Api.Photos;

namespace CampusSpace.Api.Services;

/// <summary>
/// Equipment loans (§9 Component B, UC09–UC12). Checkout and check-in are the only code that sets or clears an item's
/// OnLoan status.
/// </summary>
public interface ILoanService
{
    /// <summary>Today's (campus date) active bookings that have reservations, by start, with per-type counts.</summary>
    Task<IReadOnlyList<HandoverDto>> TodayAsync(CancellationToken ct = default);

    Task<PagedResult<LoanDto>> ListAsync(LoansQuery query, CancellationToken ct = default);

    Task<LoanDto?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Hands one item over, in one READ COMMITTED transaction that locks the booking row, then the item row. Throws
    /// BusinessRuleException (400) for an unknown booking or item and ConflictException (409) for a broken rule.
    /// </summary>
    Task<LoanDto> CheckoutAsync(CheckoutRequest request, CancellationToken ct = default);

    /// <summary>
    /// Closes an open loan and returns the item (Damaged → UnderRepair). Null when the loan does not exist. The photo is
    /// uploaded after the input checks and before the transaction (no row lock is held during the upload), and deleted
    /// again (best effort) if anything after the upload fails.
    /// </summary>
    Task<LoanDto?> CheckInAsync(long id, CheckInRequest request, CancellationToken ct = default);

    /// <summary>The loan's photo with its stored content type, or null when the loan, its photo or the object is missing.</summary>
    Task<StoredPhoto?> GetPhotoAsync(long id, CancellationToken ct = default);
}
