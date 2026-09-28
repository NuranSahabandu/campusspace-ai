using System.Globalization;
using System.Text.RegularExpressions;

namespace CampusSpace.Api.Dtos.AgentTools;

/// <summary>
/// Agent-tool time parameters: ISO 8601 with an explicit offset ("Z" or "+05:30"). A value without one is rejected
/// rather than read as server-local time, which is what DateTimeOffset model binding and System.Text.Json would do.
/// In a query string, "+" must be sent as %2B.
/// </summary>
public static partial class IsoInstant
{
    public const string Message = "must be ISO 8601 with an offset, for example 2026-10-06T14:00:00+05:30 or 2026-10-06T08:30:00Z.";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex Pattern();

    public static bool TryParse(string? value, out DateTimeOffset instant)
    {
        instant = default;
        return value is not null && Pattern().IsMatch(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out instant);
    }

    /// <summary>Only after validation has accepted the value.</summary>
    public static DateTimeOffset Parse(string? value) =>
        TryParse(value, out var instant) ? instant : throw new FormatException($"Not an ISO 8601 instant with an offset: '{value}'.");
}
