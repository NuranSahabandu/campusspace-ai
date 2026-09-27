using System.ComponentModel.DataAnnotations;
using CampusSpace.Api.Dtos.Common;

namespace CampusSpace.Api.Dtos.Quotations;

/// <summary>
/// POST /api/quotations/preview. Times may carry any offset. Quantity 0 is a line the room covers (skipped); a negative
/// quantity is a 400 from the calculator. RequesterRole is required for a Facilities Officer; a Student or Lecturer is
/// always priced as themselves and may only repeat their own role.
/// </summary>
public record QuotePreviewRequest(
    [Required, Range(1, long.MaxValue)] long? RoomId,
    [Required] DateTimeOffset? Start,
    [Required] DateTimeOffset? End,
    List<QuoteEquipmentLineRequest>? Equipment,
    [ValidRequesterRole] string? RequesterRole);

public record QuoteEquipmentLineRequest([Range(1, long.MaxValue)] long TypeId, int Quantity);

/// <summary>
/// A quote. Id, RequestId and Status are null for a preview (nothing is saved). Exempt quotes keep their lines priced
/// and have Discount = Subtotal, so Total is 0.
/// </summary>
public record QuotationDto(
    long? Id, long? RequestId, string? Status, IReadOnlyList<QuotationLineDto> Lines,
    decimal Subtotal, decimal Discount, string? DiscountReason, bool Exempt, decimal Total, string Currency = QuotationDto.Lkr)
{
    public const string Lkr = "LKR";
}

/// <summary>Kind is Room or Equipment.</summary>
public record QuotationLineDto(string Kind, string Description, decimal Qty, decimal UnitPrice, decimal LineTotal);
