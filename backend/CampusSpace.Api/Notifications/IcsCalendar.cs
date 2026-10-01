using System.Globalization;
using System.Text;

namespace CampusSpace.Api.Notifications;

/// <summary>
/// A minimal iCalendar file (RFC 5545) for the approval email: one VEVENT with METHOD:PUBLISH, times in UTC (Z), CRLF line
/// endings and TEXT escaping. No line folding: every content line is kept at most <see cref="MaxLineOctets"/> UTF-8 octets
/// by cutting the value (never inside a character or an escape pair), so no line needs folding.
/// </summary>
public static class IcsCalendar
{
    public const int MaxLineOctets = 74;
    public const int SummaryMaxChars = 60;
    private const string Crlf = "\r\n";

    public static string Uid(long bookingId) => string.Create(CultureInfo.InvariantCulture, $"booking-{bookingId}@campusspace.local");

    public static string Build(long bookingId, string purpose, string location, DateTime startUtc, DateTime endUtc, DateTime stampUtc)
    {
        var lines = new[]
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//CampusSpace AI//Bookings//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            Line("UID", Uid(bookingId)),
            Line("DTSTAMP", Utc(stampUtc)),
            Line("DTSTART", Utc(startUtc)),
            Line("DTEND", Utc(endUtc)),
            Line("SUMMARY", EscapedUnits(EmailText.Cut(purpose, SummaryMaxChars))),
            Line("LOCATION", EscapedUnits(location)),
            "END:VEVENT",
            "END:VCALENDAR",
        };
        return string.Join(Crlf, lines) + Crlf;
    }

    private static string Utc(DateTime instant) =>
        DateTime.SpecifyKind(instant, DateTimeKind.Utc).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Line(string name, string value) => Line(name, [value]);

    /// <summary>"NAME:value", with as many whole units of the value as fit in <see cref="MaxLineOctets"/> octets.</summary>
    private static string Line(string name, IEnumerable<string> units)
    {
        var line = new StringBuilder(name).Append(':');
        var octets = Encoding.UTF8.GetByteCount(line.ToString());
        foreach (var unit in units)
        {
            octets += Encoding.UTF8.GetByteCount(unit);
            if (octets > MaxLineOctets)
                break;
            line.Append(unit);
        }
        return line.ToString();
    }

    /// <summary>
    /// The TEXT value (RFC 5545 §3.3.11) as units that must not be split: an escape pair (\\ \; \, \n) or one whole
    /// character (a surrogate pair stays together). Other control characters are dropped.
    /// </summary>
    internal static IEnumerable<string> EscapedUnits(string value)
    {
        var text = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var rune in text.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '\\': yield return @"\\"; break;
                case ';': yield return @"\;"; break;
                case ',': yield return @"\,"; break;
                case '\n': yield return @"\n"; break;
                default:
                    if (!Rune.IsControl(rune))
                        yield return rune.ToString();
                    break;
            }
        }
    }
}
