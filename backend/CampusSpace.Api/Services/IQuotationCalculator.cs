namespace CampusSpace.Api.Services;

/// <summary>
/// The only place prices are computed (Component D). The agent's quote is checked against it (V09), so it is
/// deterministic: the same input on the same data gives the same result, and rules in effect never change.
/// Pure pricing: it reads the room, equipment types, policy and pricing rules, and never saves anything.
/// </summary>
public interface IQuotationCalculator
{
    /// <summary>
    /// Prices a room slot and its portable equipment. Null when the room is unknown or inactive (404).
    /// A slot that breaks V05, a bad equipment line or a missing pricing rule is a BusinessRuleException (400).
    /// </summary>
    Task<QuoteResult?> CalculateAsync(QuoteInput input, CancellationToken ct = default);
}

/// <summary><paramref name="RequesterRole"/> is one of RequesterRoles.All: it picks the pricing rule.</summary>
public sealed record QuoteInput(
    long RoomId, DateTimeOffset Start, DateTimeOffset End, string RequesterRole, IReadOnlyList<QuoteEquipmentLine> Equipment);

/// <summary>Quantity 0 is a line the room covers (addendum Change B, room_builtin): it is skipped, not priced.</summary>
public sealed record QuoteEquipmentLine(long TypeId, int Quantity);

/// <summary>Kind is one of QuotationLineKinds. EquipmentTypeId is null for the room line.</summary>
public sealed record QuoteLine(string Kind, long? EquipmentTypeId, string Description, decimal Qty, decimal UnitPrice, decimal LineTotal);

/// <summary>Total = Subtotal - Discount. An exempt quote has Discount = Subtotal and a DiscountReason.</summary>
public sealed record QuoteResult(
    IReadOnlyList<QuoteLine> Lines, decimal Subtotal, decimal Discount, string? DiscountReason, bool Exempt, decimal Total);
