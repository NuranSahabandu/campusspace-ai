using System.Globalization;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

/// <summary>
/// Room line: duration in hours × the effective room rule's HourlyRate (the rule for the room type, requester role and
/// the booking's campus date). Equipment lines: quantity × FeePerBooking (per booking, not per hour). Every line is
/// priced normally; when the room rule IsExempt, the whole quote is discounted to 0 instead (the lecturer exemption).
/// Money is decimal throughout and every line is rounded to 2 places, half away from zero, as Postgres round() does.
/// </summary>
public sealed class QuotationCalculator(
    AppDbContext db,
    IPricingRuleService pricing,
    IPolicySettingsService policy,
    IBookingWindowRules windowRules) : IQuotationCalculator
{
    public const string LecturerExemptionReason = "Lecturer exemption (academic use)";

    public async Task<QuoteResult?> CalculateAsync(QuoteInput input, CancellationToken ct = default)
    {
        var room = await db.Rooms.AsNoTracking()
            .Where(r => r.Id == input.RoomId && r.IsActive)
            .Select(r => new { r.Code, r.Type })
            .SingleOrDefaultAsync(ct);
        if (room is null)
            return null;

        // A quote for a slot that can never be booked is meaningless. Timing (V06) is not checked: the price of a slot
        // doesn't depend on when it is asked for.
        var errors = windowRules.CheckSlot(input.Start, input.End, await policy.GetAsync(ct)).ToFieldErrors("Start", "End");

        List<QuoteLine> equipmentLines = [];
        if (input.Equipment.Any(l => l.Quantity < 0))
            errors["Equipment"] = ["Quantities can't be negative."];
        else
        {
            var quantities = input.Equipment
                .GroupBy(l => l.TypeId)
                .Select(g => (TypeId: g.Key, Quantity: g.Sum(l => l.Quantity)))
                .Where(l => l.Quantity > 0)
                .ToDictionary(l => l.TypeId, l => l.Quantity);
            var ids = quantities.Keys.ToList();
            var types = await db.EquipmentTypes.AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Id, t.Code, t.Name, t.FeePerBooking })
                .ToListAsync(ct);
            var unknown = ids.Except(types.Select(t => t.Id)).Order().ToList();
            if (unknown.Count > 0)
                errors["Equipment"] = [$"Unknown equipment type: {string.Join(", ", unknown)}."];
            equipmentLines = types.OrderBy(t => t.Code).Select(t =>
            {
                var quantity = quantities[t.Id];
                return Line(QuotationLineKinds.Equipment, t.Id, $"{t.Name} x{quantity} @ LKR {Money(t.FeePerBooking)}", quantity, t.FeePerBooking);
            }).ToList();
        }

        if (errors.Count > 0)
            throw new BusinessRuleException(errors);

        var rule = await pricing.GetEffectiveRuleAsync(room.Type, input.RequesterRole, input.Start, ct)
            ?? throw new BusinessRuleException("Room",
                $"No pricing rule for {RoomTypes.Label(room.Type)} / {input.RequesterRole} bookings on {CampusTime.DateOf(input.Start):yyyy-MM-dd}.");

        var hours = Hours(input.Start, input.End);
        var roomLine = Line(QuotationLineKinds.Room, null,
            $"{RoomTypes.Label(room.Type)} {room.Code}, {hours.ToString("0.##", CultureInfo.InvariantCulture)} h @ LKR {Money(rule.HourlyRate)}",
            hours, rule.HourlyRate);

        List<QuoteLine> lines = [roomLine, .. equipmentLines];
        var subtotal = lines.Sum(l => l.LineTotal);
        var discount = rule.IsExempt ? subtotal : 0m;
        return new QuoteResult(lines, subtotal, discount, rule.IsExempt ? LecturerExemptionReason : null, rule.IsExempt, subtotal - discount);
    }

    /// <summary>
    /// The duration in hours, rounded to 2 places first, so the stored Qty and the LineTotal computed from it always agree
    /// with CK_QuotationLines_LineTotal (20 minutes is 0.33 h), whatever slot granularity the policy allows.
    /// </summary>
    public static decimal Hours(DateTimeOffset start, DateTimeOffset end) =>
        Math.Round((decimal)(end - start).TotalMinutes / 60m, 2, MidpointRounding.AwayFromZero);

    /// <summary>One line with LineTotal = round(Qty × UnitPrice, 2), half away from zero.</summary>
    public static QuoteLine Line(string kind, long? equipmentTypeId, string description, decimal qty, decimal unitPrice) =>
        new(kind, equipmentTypeId, description, qty, unitPrice, Math.Round(qty * unitPrice, 2, MidpointRounding.AwayFromZero));

    /// <summary>"1,500" or "1,500.5": thousands separators, no trailing zeros (Appendix B style).</summary>
    private static string Money(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture);
}
