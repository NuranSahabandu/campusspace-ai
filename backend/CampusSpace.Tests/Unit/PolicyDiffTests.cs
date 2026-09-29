using System.Text.Json;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

public class PolicyDiffTests
{
    private const string Hours = """{"mon":{"open":"08:00","close":"20:00"},"sun":null}""";

    private static string Snapshot(string hours = Hours, string ratio = "3", bool withCheckout = true) => $$"""
        {"opening_hours":{{hours}},"min_lead_time_hours":48,"max_advance_days_student":60,"max_advance_days_lecturer":90,
         "max_duration_hours":8,"max_capacity_ratio":{{ratio}},"slot_granularity_minutes":30,"free_cancellation_hours":24,
         "max_open_requests":3{{(withCheckout ? ",\"checkout_window_minutes\":30" : "")}}}
        """;

    /// <summary>Current values shaped like PolicySnapshot.ToPublicValues (numbers, opening_hours as an object).</summary>
    private static Dictionary<string, object?> Current(Action<Dictionary<string, object?>>? change = null)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(Snapshot())!;
        values[PolicyKeys.MaxCapacityRatio] = 3.0m;
        change?.Invoke(values);
        return values;
    }

    [Fact]
    public void Nothing_changed_when_numbers_are_equal_by_value() =>
        PolicyDiff.ChangedKeys(Snapshot(), Current()).Should().BeEmpty();

    [Fact]
    public void A_changed_number_and_a_changed_opening_hours_are_listed_in_PolicyKeys_order()
    {
        var current = Current(v =>
        {
            v[PolicyKeys.MinLeadTimeHours] = 24;
            v[PolicyKeys.OpeningHours] = JsonSerializer.Deserialize<JsonElement>("""{"mon":{"open":"09:00","close":"20:00"},"sun":null}""");
        });

        PolicyDiff.ChangedKeys(Snapshot(), current).Should().Equal(PolicyKeys.OpeningHours, PolicyKeys.MinLeadTimeHours);
    }

    [Fact]
    public void A_day_that_opens_counts_as_changed() =>
        PolicyDiff.ChangedKeys(Snapshot(), Current(v => v[PolicyKeys.OpeningHours] =
                JsonSerializer.Deserialize<JsonElement>("""{"mon":{"open":"08:00","close":"20:00"},"sun":{"open":"08:00","close":"12:00"}}""")))
            .Should().Equal(PolicyKeys.OpeningHours);

    [Fact]
    public void A_key_missing_from_the_snapshot_counts_as_changed() =>
        PolicyDiff.ChangedKeys(Snapshot(withCheckout: false), Current()).Should().Equal(PolicyKeys.CheckoutWindowMinutes);

    [Fact]
    public void No_snapshot_means_nothing_to_compare() =>
        PolicyDiff.ChangedKeys(null, Current()).Should().BeEmpty();
}
