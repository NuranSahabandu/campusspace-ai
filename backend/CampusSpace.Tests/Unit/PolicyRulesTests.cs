using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

public class PolicyRulesTests
{
    private static Dictionary<string, string> Defaults() => PolicySettingDefaults.All.ToDictionary(d => d.Key, d => d.Value);

    private static string Hours(string weekday, string sat = """{"open":"08:00","close":"16:00"}""", string sun = "null") =>
        $$"""{"mon":{{weekday}},"tue":{{weekday}},"wed":{{weekday}},"thu":{{weekday}},"fri":{{weekday}},"sat":{{sat}},"sun":{{sun}}}""";

    /// <summary>Parses and validates the defaults with <paramref name="changes"/> applied; returns the errors.</summary>
    private static Dictionary<string, List<string>> Check(params (string Key, string Value)[] changes)
    {
        var values = Defaults();
        foreach (var (key, value) in changes)
            values[key] = value;
        var errors = new Dictionary<string, List<string>>();
        var snapshot = PolicyRules.Parse(values, errors);
        if (snapshot is not null)
            PolicyRules.Validate(snapshot, errors);
        return errors;
    }

    [Fact]
    public void Defaults_parse_into_the_typed_snapshot_and_are_valid()
    {
        var errors = new Dictionary<string, List<string>>();
        var p = PolicyRules.Parse(Defaults(), errors)!;
        PolicyRules.Validate(p, errors);

        errors.Should().BeEmpty();
        p.OpeningHours[DayOfWeek.Monday].Should().Be(new DayHours(new TimeOnly(8, 0), new TimeOnly(20, 0)));
        p.OpeningHours[DayOfWeek.Saturday].Should().Be(new DayHours(new TimeOnly(8, 0), new TimeOnly(16, 0)));
        p.OpeningHours[DayOfWeek.Sunday].Should().BeNull();
        p.Should().Match<PolicySnapshot>(s => s.MinLeadTimeHours == 48 && s.MaxAdvanceDaysStudent == 60
            && s.MaxAdvanceDaysLecturer == 90 && s.MaxDurationHours == 8 && s.MaxCapacityRatio == 3m
            && s.SlotGranularityMinutes == 30 && s.FreeCancellationHours == 24 && s.MaxOpenRequests == 3);
    }

    [Fact]
    public void Canonical_values_of_the_defaults_equal_the_stored_text()
    {
        var errors = new Dictionary<string, List<string>>();
        var p = PolicyRules.Parse(Defaults(), errors)!;

        PolicyRules.ToValues(p).Should().BeEquivalentTo(Defaults());
    }

    [Fact]
    public void Equivalent_spellings_have_the_same_canonical_value()
    {
        var values = Defaults();
        values[PolicyKeys.MaxCapacityRatio] = "3.0";
        values[PolicyKeys.MinLeadTimeHours] = " 48 ";
        values[PolicyKeys.OpeningHours] = values[PolicyKeys.OpeningHours].Replace(",", ", ");
        var p = PolicyRules.Parse(values, [])!;

        PolicyRules.ToValues(p).Should().BeEquivalentTo(Defaults());
    }

    [Theory]
    [InlineData(PolicyKeys.MinLeadTimeHours, "abc")]
    [InlineData(PolicyKeys.MinLeadTimeHours, "4.5")]
    [InlineData(PolicyKeys.MaxCapacityRatio, "three")]
    [InlineData(PolicyKeys.OpeningHours, "not json")]
    [InlineData(PolicyKeys.OpeningHours, "[]")]
    public void Unparseable_values_are_errors_on_their_key(string key, string value)
    {
        Check((key, value)).Should().ContainKey(key).And.HaveCount(1);
    }

    [Theory]
    [InlineData("""{"open":"8:00","close":"20:00"}""")]
    [InlineData("""{"open":"08:00","close":"24:00"}""")]
    [InlineData("""{"open":"08:00"}""")]
    [InlineData("""{"open":"08:00","close":"20:00","note":"x"}""")]
    [InlineData("\"closed\"")]
    public void Bad_day_shapes_or_time_formats_are_errors_on_opening_hours(string day)
    {
        Check((PolicyKeys.OpeningHours, Hours(day))).Keys.Should().Equal(PolicyKeys.OpeningHours);
    }

    [Fact]
    public void A_missing_or_unknown_day_is_an_error()
    {
        Check((PolicyKeys.OpeningHours, """{"mon":null}""")).Keys.Should().Equal(PolicyKeys.OpeningHours);
        Check((PolicyKeys.OpeningHours, Hours("null").Replace("}", ",\"hol\":null}")))
            .Keys.Should().Equal(PolicyKeys.OpeningHours);
    }

    [Fact]
    public void Open_must_be_before_close()
    {
        var errors = Check((PolicyKeys.OpeningHours, Hours("""{"open":"08:00","close":"20:00"}""", sat: """{"open":"16:00","close":"16:00"}""")));

        errors.Keys.Should().Equal(PolicyKeys.OpeningHours);
        errors[PolicyKeys.OpeningHours].Single().Should().StartWith("sat:");
    }

    [Fact]
    public void Times_must_be_on_the_granularity()
    {
        Check((PolicyKeys.OpeningHours, Hours("""{"open":"08:15","close":"20:00"}"""))).Keys.Should().Equal(PolicyKeys.OpeningHours);
        Check((PolicyKeys.SlotGranularityMinutes, "15"), (PolicyKeys.OpeningHours, Hours("""{"open":"08:15","close":"20:00"}""")))
            .Should().BeEmpty();
    }

    [Fact]
    public void Changing_granularity_to_60_is_checked_against_the_opening_times()
    {
        var hours = Hours("""{"open":"08:30","close":"20:00"}""");
        Check((PolicyKeys.OpeningHours, hours)).Should().BeEmpty();

        Check((PolicyKeys.OpeningHours, hours), (PolicyKeys.SlotGranularityMinutes, "60")).Keys.Should().Equal(PolicyKeys.OpeningHours);
    }

    [Theory]
    [InlineData("20")]
    [InlineData("0")]
    [InlineData("45")]
    public void Granularity_must_be_15_30_or_60(string value)
    {
        Check((PolicyKeys.SlotGranularityMinutes, value)).Keys.Should().Equal(PolicyKeys.SlotGranularityMinutes);
    }

    [Fact]
    public void At_least_one_day_must_be_open()
    {
        Check((PolicyKeys.OpeningHours, Hours("null", sat: "null"))).Keys.Should().Contain(PolicyKeys.OpeningHours);
    }

    [Fact]
    public void Duration_cannot_exceed_the_longest_open_day()
    {
        // Longest day 08:00-20:00 is 12 h.
        Check((PolicyKeys.MaxDurationHours, "12")).Should().BeEmpty();
        Check((PolicyKeys.MaxDurationHours, "13")).Keys.Should().Equal(PolicyKeys.MaxDurationHours);
        // Shorter days in the same request: 09:00-15:00 is 6 h, so the default 8 h no longer fits.
        Check((PolicyKeys.OpeningHours, Hours("""{"open":"09:00","close":"15:00"}""", sat: "null")))
            .Keys.Should().Equal(PolicyKeys.MaxDurationHours);
    }

    [Theory]
    [InlineData(PolicyKeys.MinLeadTimeHours, "-1")]
    [InlineData(PolicyKeys.MinLeadTimeHours, "721")]
    [InlineData(PolicyKeys.MaxAdvanceDaysStudent, "0")]
    [InlineData(PolicyKeys.MaxAdvanceDaysStudent, "366")]
    [InlineData(PolicyKeys.MaxAdvanceDaysLecturer, "0")]
    [InlineData(PolicyKeys.MaxAdvanceDaysLecturer, "366")]
    [InlineData(PolicyKeys.MaxDurationHours, "0")]
    [InlineData(PolicyKeys.MaxDurationHours, "25")]
    [InlineData(PolicyKeys.MaxCapacityRatio, "0.9")]
    [InlineData(PolicyKeys.MaxCapacityRatio, "10.1")]
    [InlineData(PolicyKeys.MaxCapacityRatio, "2.55")]
    [InlineData(PolicyKeys.FreeCancellationHours, "-1")]
    [InlineData(PolicyKeys.FreeCancellationHours, "721")]
    [InlineData(PolicyKeys.MaxOpenRequests, "0")]
    [InlineData(PolicyKeys.MaxOpenRequests, "21")]
    public void Out_of_range_numbers_are_errors_on_their_key(string key, string value)
    {
        Check((key, value)).Keys.Should().Equal(key);
    }

    [Theory]
    [InlineData(PolicyKeys.MinLeadTimeHours, "0")]
    [InlineData(PolicyKeys.MinLeadTimeHours, "720")]
    [InlineData(PolicyKeys.MaxAdvanceDaysStudent, "365")]
    [InlineData(PolicyKeys.MaxDurationHours, "1")]
    [InlineData(PolicyKeys.MaxCapacityRatio, "1")]
    [InlineData(PolicyKeys.MaxCapacityRatio, "2.5")]
    [InlineData(PolicyKeys.MaxCapacityRatio, "10")]
    [InlineData(PolicyKeys.MaxOpenRequests, "20")]
    public void Boundary_values_are_accepted(string key, string value)
    {
        Check((key, value)).Should().BeEmpty();
    }

    [Fact]
    public void A_missing_key_is_an_error()
    {
        var values = Defaults();
        values.Remove(PolicyKeys.MaxOpenRequests);
        var errors = new Dictionary<string, List<string>>();

        PolicyRules.Parse(values, errors).Should().BeNull();
        errors.Keys.Should().Equal(PolicyKeys.MaxOpenRequests);
    }

    [Fact]
    public void Public_values_are_typed()
    {
        var p = PolicyRules.Parse(Defaults(), [])!;

        var values = p.ToPublicValues();

        values.Keys.Should().BeEquivalentTo(PolicyKeys.All);
        values[PolicyKeys.MinLeadTimeHours].Should().Be(48);
        values[PolicyKeys.MaxCapacityRatio].Should().Be(3m);
        System.Text.Json.JsonSerializer.Serialize(values[PolicyKeys.OpeningHours]).Should().Be(PolicySettingDefaults.OpeningHoursJson);
    }
}
