using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Services;

/// <summary>
/// Parses PolicySettings values into a <see cref="PolicySnapshot"/> and checks the snapshot as a whole. Pure functions:
/// PolicySettingsService uses them for GET (a failure is a server bug) and PUT (a failure is a 400).
/// Errors are keyed by setting key. The limits below bound what an officer may set; they are not booking policy.
/// </summary>
public static partial class PolicyRules
{
    public static readonly IReadOnlyList<int> AllowedGranularities = [15, 30, 60];

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex TimePattern();

    /// <summary>Parses every key of <paramref name="values"/>. Returns null and fills <paramref name="errors"/> if any fails.</summary>
    public static PolicySnapshot? Parse(IReadOnlyDictionary<string, string> values, Dictionary<string, List<string>> errors)
    {
        foreach (var key in PolicyKeys.All.Where(k => !values.ContainsKey(k)))
            Add(errors, key, "Setting is missing.");

        var ints = new Dictionary<string, int>();
        decimal ratio = 0;
        ImmutableDictionary<DayOfWeek, DayHours?>? hours = null;
        foreach (var (key, value) in values)
        {
            switch (PolicyKeys.TypeOf.GetValueOrDefault(key))
            {
                case PolicyValueTypes.Int when int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i):
                    ints[key] = i;
                    break;
                case PolicyValueTypes.Int:
                    Add(errors, key, "Must be a whole number.");
                    break;
                case PolicyValueTypes.Decimal when decimal.TryParse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var d):
                    ratio = d;
                    break;
                case PolicyValueTypes.Decimal:
                    Add(errors, key, "Must be a number.");
                    break;
                case PolicyValueTypes.Json:
                    hours = ParseOpeningHours(value, errors);
                    break;
                default:
                    Add(errors, key, "Unknown setting.");
                    break;
            }
        }

        if (errors.Count > 0 || hours is null)
            return null;
        return new PolicySnapshot(
            hours,
            ints[PolicyKeys.MinLeadTimeHours],
            ints[PolicyKeys.MaxAdvanceDaysStudent],
            ints[PolicyKeys.MaxAdvanceDaysLecturer],
            ints[PolicyKeys.MaxDurationHours],
            ratio,
            ints[PolicyKeys.SlotGranularityMinutes],
            ints[PolicyKeys.FreeCancellationHours],
            ints[PolicyKeys.MaxOpenRequests],
            ints[PolicyKeys.CheckoutWindowMinutes]);
    }

    /// <summary>
    /// Checks the settings together, so a change to one key is checked against the new values of the others
    /// (for example opening times against the new granularity).
    /// </summary>
    public static void Validate(PolicySnapshot p, Dictionary<string, List<string>> errors)
    {
        var granularity = p.SlotGranularityMinutes;
        var granularityOk = AllowedGranularities.Contains(granularity);
        if (!granularityOk)
            Add(errors, PolicyKeys.SlotGranularityMinutes, $"Must be one of: {string.Join(", ", AllowedGranularities)}.");

        foreach (var (name, day) in PolicySnapshot.Days)
        {
            if (p.OpeningHours[day] is not { } h)
                continue;
            if (h.Open >= h.Close)
                Add(errors, PolicyKeys.OpeningHours, $"{name}: opening time must be before closing time.");
            if (granularityOk && (Minutes(h.Open) % granularity != 0 || Minutes(h.Close) % granularity != 0))
                Add(errors, PolicyKeys.OpeningHours, $"{name}: times must be on a {granularity}-minute boundary.");
        }
        var openDays = p.OpeningHours.Values.OfType<DayHours>().Where(h => h.Open < h.Close).ToList();
        if (p.OpeningHours.Values.All(h => h is null))
            Add(errors, PolicyKeys.OpeningHours, "At least one day must be open.");

        Range(errors, PolicyKeys.MinLeadTimeHours, p.MinLeadTimeHours, 0, 720);
        Range(errors, PolicyKeys.MaxAdvanceDaysStudent, p.MaxAdvanceDaysStudent, 1, 365);
        Range(errors, PolicyKeys.MaxAdvanceDaysLecturer, p.MaxAdvanceDaysLecturer, 1, 365);
        Range(errors, PolicyKeys.FreeCancellationHours, p.FreeCancellationHours, 0, 720);
        Range(errors, PolicyKeys.MaxOpenRequests, p.MaxOpenRequests, 1, 20);
        Range(errors, PolicyKeys.CheckoutWindowMinutes, p.CheckoutWindowMinutes, 0, 240);

        if (p.MaxCapacityRatio is < 1 or > 10)
            Add(errors, PolicyKeys.MaxCapacityRatio, "Must be between 1 and 10.");
        else if (decimal.Round(p.MaxCapacityRatio, 1) != p.MaxCapacityRatio)
            Add(errors, PolicyKeys.MaxCapacityRatio, "Can have at most 1 decimal place.");

        if (Range(errors, PolicyKeys.MaxDurationHours, p.MaxDurationHours, 1, 24))
        {
            // Always true while durations are whole hours and granularities divide 60; kept so the rule survives
            // a change to either.
            if (granularityOk && p.MaxDurationHours * 60 % granularity != 0)
                Add(errors, PolicyKeys.MaxDurationHours, $"Must be a multiple of {granularity} minutes.");
            var longest = openDays.Count == 0 ? 0 : openDays.Max(h => Minutes(h.Close) - Minutes(h.Open));
            if (openDays.Count > 0 && p.MaxDurationHours * 60 > longest)
                Add(errors, PolicyKeys.MaxDurationHours, $"Can't be longer than the longest open day ({longest / 60.0:0.##} h).");
        }
    }

    /// <summary>
    /// The stored text of every key: ints invariant, the ratio without trailing zeros, opening_hours in day order.
    /// A PUT compares these with the stored values, so "3.0" for "3" is not a change.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ToValues(PolicySnapshot p) => new Dictionary<string, string>
    {
        [PolicyKeys.OpeningHours] = JsonSerializer.Serialize(p.OpeningHoursJsonShape()),
        [PolicyKeys.MinLeadTimeHours] = Text(p.MinLeadTimeHours),
        [PolicyKeys.MaxAdvanceDaysStudent] = Text(p.MaxAdvanceDaysStudent),
        [PolicyKeys.MaxAdvanceDaysLecturer] = Text(p.MaxAdvanceDaysLecturer),
        [PolicyKeys.MaxDurationHours] = Text(p.MaxDurationHours),
        [PolicyKeys.MaxCapacityRatio] = p.MaxCapacityRatio.ToString("0.#############", CultureInfo.InvariantCulture),
        [PolicyKeys.SlotGranularityMinutes] = Text(p.SlotGranularityMinutes),
        [PolicyKeys.FreeCancellationHours] = Text(p.FreeCancellationHours),
        [PolicyKeys.MaxOpenRequests] = Text(p.MaxOpenRequests),
        [PolicyKeys.CheckoutWindowMinutes] = Text(p.CheckoutWindowMinutes),
    };

    /// <summary>{"mon":{"open":"HH:mm","close":"HH:mm"}|null, ... "sun":...}: exactly the seven days, nothing else.</summary>
    private static ImmutableDictionary<DayOfWeek, DayHours?>? ParseOpeningHours(string value, Dictionary<string, List<string>> errors)
    {
        const string key = PolicyKeys.OpeningHours;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
            Add(errors, key, "Must be valid JSON.");
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Add(errors, key, "Must be an object with the days mon to sun.");
                return null;
            }

            var names = PolicySnapshot.Days.Select(d => d.Name).ToHashSet();
            var unknown = root.EnumerateObject().Select(p => p.Name).Where(n => !names.Contains(n)).ToList();
            if (unknown.Count > 0)
                Add(errors, key, $"Unknown days: {string.Join(", ", unknown)}.");

            var builder = ImmutableDictionary.CreateBuilder<DayOfWeek, DayHours?>();
            var ok = unknown.Count == 0;
            foreach (var (name, day) in PolicySnapshot.Days)
            {
                if (!root.TryGetProperty(name, out var entry))
                {
                    Add(errors, key, $"{name}: missing (use null for closed).");
                    ok = false;
                    continue;
                }
                if (entry.ValueKind == JsonValueKind.Null)
                {
                    builder[day] = null;
                    continue;
                }

                var open = TimeOf(entry, "open");
                var close = TimeOf(entry, "close");
                if (entry.ValueKind != JsonValueKind.Object || entry.EnumerateObject().Count() != 2 || open is null || close is null)
                {
                    Add(errors, key, $"{name}: must be null or {{\"open\":\"HH:mm\",\"close\":\"HH:mm\"}}.");
                    ok = false;
                    continue;
                }
                builder[day] = new DayHours(open.Value, close.Value);
            }
            return ok ? builder.ToImmutable() : null;
        }
    }

    private static TimeOnly? TimeOf(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty(name, out var p)
        && p.ValueKind == JsonValueKind.String
        && p.GetString() is { } s
        && TimePattern().IsMatch(s)
            ? TimeOnly.ParseExact(s, "HH:mm", CultureInfo.InvariantCulture)
            : null;

    private static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Adds an error if <paramref name="value"/> is outside [min, max]; returns whether it is inside.</summary>
    private static bool Range(Dictionary<string, List<string>> errors, string key, int value, int min, int max)
    {
        if (value >= min && value <= max)
            return true;
        Add(errors, key, $"Must be between {min} and {max}.");
        return false;
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
            errors[key] = list = [];
        list.Add(message);
    }
}
