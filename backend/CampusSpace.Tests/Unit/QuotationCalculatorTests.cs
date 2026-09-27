using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>The pure rounding helpers: Qty is rounded first, and LineTotal is computed from the rounded Qty.</summary>
public class QuotationCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 10, 0, 0, TimeSpan.FromMinutes(330));

    [Theory]
    [InlineData(20, "0.33")]
    [InlineData(40, "0.67")]
    [InlineData(90, "1.5")]
    [InlineData(180, "3")]
    public void Hours_are_rounded_to_two_places(int minutes, string expected) =>
        QuotationCalculator.Hours(Start, Start.AddMinutes(minutes)).Should().Be(decimal.Parse(expected));

    [Fact]
    public void Line_total_uses_the_rounded_qty_and_rounds_half_away_from_zero()
    {
        // 0.33 × 1500 = 495 (not 500, which 20/60 × 1500 would give).
        QuotationCalculator.Line(QuotationLineKinds.Room, null, "x", QuotationCalculator.Hours(Start, Start.AddMinutes(20)), 1500m)
            .LineTotal.Should().Be(495.00m);
        QuotationCalculator.Line(QuotationLineKinds.Room, null, "x", 1.5m, 333.33m).LineTotal.Should().Be(500.00m);
        QuotationCalculator.Line(QuotationLineKinds.Equipment, 1, "x", 3m, 0.005m).LineTotal.Should().Be(0.02m);
    }
}
