using System.Text;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Notifications;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>The minimal .ics (Task 5.2): one VEVENT, UTC times, CRLF, TEXT escaping and no line longer than 74 octets.</summary>
public class IcsCalendarTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 10, 0, 0, CampusTime.Offset);
    private static readonly DateTimeOffset End = new(2026, 11, 9, 13, 0, 0, CampusTime.Offset);
    private static readonly DateTime Stamp = new(2026, 10, 1, 8, 15, 30, DateTimeKind.Utc);

    private static string Build(string purpose = "AI Club Workshop", string location = "Computer Lab A301, Main Building", long id = 42) =>
        IcsCalendar.Build(id, purpose, location, Start.UtcDateTime, End.UtcDateTime, Stamp);

    private static string[] Lines(string ics) => ics.Split("\r\n")[..^1];

    private static string Value(string ics, string name) => Lines(ics).Single(l => l.StartsWith(name + ":", StringComparison.Ordinal))[(name.Length + 1)..];

    [Fact]
    public void It_is_one_published_event_with_utc_times_for_a_campus_time_booking()
    {
        var ics = Build();

        Lines(ics).Should().Equal(
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//CampusSpace AI//Bookings//EN", "CALSCALE:GREGORIAN", "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            "UID:booking-42@campusspace.local",
            "DTSTAMP:20261001T081530Z",
            // 10:00–13:00 campus time (UTC+05:30) is 04:30–07:30 UTC.
            "DTSTART:20261109T043000Z",
            "DTEND:20261109T073000Z",
            "SUMMARY:AI Club Workshop",
            "LOCATION:Computer Lab A301\\, Main Building",
            "END:VEVENT", "END:VCALENDAR");
    }

    [Fact]
    public void Every_line_ends_with_CRLF_and_there_is_no_bare_LF_or_CR()
    {
        var ics = Build(purpose: "Line one\nline two\r\nline three\rfour");

        ics.Should().EndWith("\r\n");
        ics.Replace("\r\n", "", StringComparison.Ordinal).Should().NotContain("\n").And.NotContain("\r");
    }

    [Fact]
    public void Text_values_escape_backslash_semicolon_comma_and_newline()
    {
        var ics = Build(purpose: @"a\b;c,d" + "\ne", location: "Room; 1, Hall\\2");

        Value(ics, "SUMMARY").Should().Be(@"a\\b\;c\,d\ne");
        Value(ics, "LOCATION").Should().Be(@"Room\; 1\, Hall\\2");
    }

    [Fact]
    public void Other_control_characters_are_dropped()
    {
        Value(Build(purpose: "Tab\there\u0007bell"), "SUMMARY").Should().Be("Tabherebell");
    }

    [Fact]
    public void The_summary_is_the_purpose_cut_to_60_characters()
    {
        Value(Build(purpose: new string('x', 100)), "SUMMARY").Should().Be(new string('x', 60));
    }

    [Theory]
    [InlineData("ශ්‍රී ලංකා පරිගණක සමාජයේ වාර්ෂික තාක්ෂණ සම්මන්ත්‍රණය සහ ප්‍රදර්ශනය 2026 🎉🎉🎉 සියලු සාමාජිකයින් සඳහා")]
    [InlineData(@";;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;")]
    [InlineData("🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉🎉")]
    public void Every_line_stays_at_or_under_74_octets_without_folding_and_never_splits_a_character_or_escape(string text)
    {
        var longRoom = string.Concat(Enumerable.Repeat("Very Long Lecture Theatre, ", 8)) + text;
        var ics = Build(purpose: text + text + text, location: longRoom);

        foreach (var line in Lines(ics))
        {
            Encoding.UTF8.GetByteCount(line).Should().BeLessThanOrEqualTo(IcsCalendar.MaxLineOctets, line);
            line.Should().NotStartWith(" ", "nothing is folded");
            // A cut never leaves half an escape pair or a lone surrogate behind.
            var escapes = line.Length - line.TrimEnd('\\').Length;
            (escapes % 2).Should().Be(0, line);
            char.IsHighSurrogate(line[^1]).Should().BeFalse(line);
            Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(line)).Should().Be(line, "the line is valid UTF-8");
        }
    }

    [Fact]
    public void The_uid_is_stable_for_a_booking()
    {
        Value(Build(id: 7), "UID").Should().Be(Value(Build(id: 7, purpose: "Other"), "UID")).And.Be("booking-7@campusspace.local");
        IcsCalendar.Uid(8).Should().NotBe(IcsCalendar.Uid(7));
    }
}
