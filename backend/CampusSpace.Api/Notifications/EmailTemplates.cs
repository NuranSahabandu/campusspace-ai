using System.Globalization;
using System.Net;
using System.Text;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Notifications;

/// <summary>
/// Everything an email may say (plan §14 data minimisation): the requester's name, the purpose, the slot in campus time,
/// and for an approval the room, building and quote total. Never ids, roles, requester notes, agent output or internal
/// reasons. <see cref="Reason"/> is the officer's own reason, set only for Rejected and CancelledByOfficer.
/// </summary>
public sealed record EmailModel(
    string Kind, string RequesterName, string Purpose, DateTimeOffset Start, DateTimeOffset End,
    string? Location = null, decimal? Total = null, bool IsExempt = false, string? Reason = null);

public sealed record RenderedEmail(string Subject, string Html, string Text);

/// <summary>
/// The fixed templates, one per kind. The wording lives only here (never in a request or a model's output). Every value is
/// HTML-encoded in the HTML part, and the plain-text part says the same thing.
/// </summary>
public static class EmailTemplates
{
    public const int SubjectPurposeMaxChars = 60;
    public const string RedirectedPrefix = "[Redirected] ";

    private const string Footer = "This is an automated message from CampusSpace AI. Please don't reply to it.";

    public static RenderedEmail Render(EmailModel m)
    {
        var purpose = EmailText.Cut(EmailText.SingleLine(m.Purpose), SubjectPurposeMaxChars);
        var when = FormatSlot(m.Start, m.End);
        var (subject, intro, rows, closing) = m.Kind switch
        {
            NotificationKinds.Approved => (
                $"Booking confirmed: {purpose}",
                "Your booking request has been approved. Your room is booked.",
                Rows(("Purpose", m.Purpose), ("Room", m.Location ?? ""), ("When", when), ("Total", FormatTotal(m.Total, m.IsExempt))),
                "The attached calendar file (booking.ics) adds the booking to your calendar."),
            NotificationKinds.Rejected => (
                $"Booking request not approved: {purpose}",
                "The Facilities Officer did not approve your booking request.",
                Rows(("Purpose", m.Purpose), ("When", when), ("Reason", m.Reason ?? "")),
                "You can submit a new request in the CampusSpace app."),
            NotificationKinds.Closed => (
                $"Booking request closed: {purpose}",
                "Your booking request was closed automatically, because it can no longer go ahead as requested.",
                Rows(("Purpose", m.Purpose), ("When", when)),
                "You can submit a new request with a different time in the CampusSpace app."),
            NotificationKinds.RevisionRequested => (
                $"Booking request being re-planned: {purpose}",
                "A new proposal is being prepared for your booking request.",
                Rows(("Purpose", m.Purpose), ("When", when)),
                "You don't need to do anything. We'll email you again when it has been decided."),
            NotificationKinds.CancelledByOfficer => (
                $"Booking request cancelled: {purpose}",
                "The Facilities Officer cancelled your booking request.",
                Rows(("Purpose", m.Purpose), ("When", when), ("Reason", m.Reason ?? "")),
                "If you added this booking to your calendar, please remove it."),
            _ => throw new ArgumentOutOfRangeException(nameof(m), m.Kind, "Unknown notification kind"),
        };

        var greeting = $"Hello {EmailText.SingleLine(m.RequesterName)},";
        return new RenderedEmail(EmailText.SingleLine(subject), Html(greeting, intro, rows, closing), Text(greeting, intro, rows, closing));
    }

    /// <summary>"Mon 9 Nov 2026, 10:00–13:00 (campus time, UTC+05:30)"; both dates when it crosses midnight.</summary>
    public static string FormatSlot(DateTimeOffset start, DateTimeOffset end)
    {
        var s = start.ToOffset(CampusTime.Offset);
        var e = end.ToOffset(CampusTime.Offset);
        var day = s.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);
        var range = s.Date == e.Date
            ? $"{day}, {s.ToString("HH:mm", CultureInfo.InvariantCulture)}–{e.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : $"{day} {s.ToString("HH:mm", CultureInfo.InvariantCulture)} – {e.ToString("ddd d MMM yyyy HH:mm", CultureInfo.InvariantCulture)}";
        return $"{range} (campus time, UTC+05:30)";
    }

    public static string FormatTotal(decimal? total, bool isExempt) =>
        $"LKR {(total ?? 0m).ToString("N2", CultureInfo.InvariantCulture)}{(isExempt ? " (fee-exempt)" : "")}";

    private static IReadOnlyList<(string Label, string Value)> Rows(params (string Label, string Value)[] rows) => rows;

    private static string Html(string greeting, string intro, IReadOnlyList<(string Label, string Value)> rows, string closing)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"font-family:Arial,Helvetica,sans-serif;color:#1f2933;line-height:1.5\">");
        html.Append("<p>").Append(Encode(greeting)).Append("</p>");
        html.Append("<p>").Append(Encode(intro)).Append("</p>");
        html.Append("<table style=\"border-collapse:collapse\">");
        foreach (var (label, value) in rows)
        {
            html.Append("<tr><th style=\"text-align:left;padding:4px 12px 4px 0;vertical-align:top\">").Append(Encode(label))
                .Append("</th><td style=\"padding:4px 0;white-space:pre-wrap\">").Append(Encode(value)).Append("</td></tr>");
        }
        html.Append("</table>");
        html.Append("<p>").Append(Encode(closing)).Append("</p>");
        html.Append("<p style=\"color:#6b7280;font-size:12px\">").Append(Encode(Footer)).Append("</p>");
        html.Append("</body></html>");
        return html.ToString();
    }

    private static string Text(string greeting, string intro, IReadOnlyList<(string Label, string Value)> rows, string closing)
    {
        var text = new StringBuilder();
        text.Append(greeting).Append("\n\n").Append(intro).Append("\n\n");
        foreach (var (label, value) in rows)
            text.Append(label).Append(": ").Append(value).Append('\n');
        text.Append('\n').Append(closing).Append("\n\n").Append(Footer).Append('\n');
        return text.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
