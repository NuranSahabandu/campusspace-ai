using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Notifications;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>The fixed templates (Task 5.2): HTML-encoded values, a plain-text part, and only the minimised data.</summary>
public class EmailTemplatesTests
{
    private const string Hostile = "<script>alert(\"x\")</script> & <b>bold</b>";
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 10, 0, 0, CampusTime.Offset);

    private static EmailModel Model(string kind, string purpose = "AI Club Workshop", string? reason = null) =>
        new(kind, "Nimal Perera", purpose, Start, Start.AddHours(3), "Computer Lab A301, Main Building", 5500m, false, reason);

    [Theory]
    [InlineData(NotificationKinds.Approved)]
    [InlineData(NotificationKinds.Rejected)]
    [InlineData(NotificationKinds.Closed)]
    [InlineData(NotificationKinds.RevisionRequested)]
    [InlineData(NotificationKinds.CancelledByOfficer)]
    public void A_hostile_purpose_and_reason_are_encoded_in_the_html(string kind)
    {
        var email = EmailTemplates.Render(Model(kind, purpose: Hostile, reason: Hostile) with { RequesterName = Hostile });

        email.Html.Should().NotContain("<script>").And.NotContain("<b>bold");
        email.Html.Should().Contain("&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &lt;b&gt;bold&lt;/b&gt;");
        email.Text.Should().Contain("Hello ").And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_subject_is_one_line_with_the_purpose_cut_to_60_characters()
    {
        var email = EmailTemplates.Render(Model(NotificationKinds.Approved, purpose: "Line one\r\nBcc: x@example.com " + new string('y', 80)));

        email.Subject.Should().NotContain("\n").And.NotContain("\r");
        email.Subject.Should().StartWith("Booking confirmed: Line one Bcc: x@example.com ");
        email.Subject.Length.Should().BeLessThanOrEqualTo("Booking confirmed: ".Length + EmailTemplates.SubjectPurposeMaxChars);
    }

    [Fact]
    public void Approved_shows_the_room_the_campus_time_slot_and_the_total()
    {
        var email = EmailTemplates.Render(Model(NotificationKinds.Approved));

        foreach (var body in new[] { email.Html, email.Text })
        {
            body.Should().Contain("Nimal Perera").And.Contain("AI Club Workshop").And.Contain("Computer Lab A301, Main Building");
            body.Should().Contain("Mon 9 Nov 2026, 10:00–13:00 (campus time, UTC+05:30)").And.Contain("LKR 5,500.00");
            body.Should().Contain("booking.ics");
        }
    }

    [Fact]
    public void An_exempt_approval_says_fee_exempt()
    {
        EmailTemplates.Render(Model(NotificationKinds.Approved) with { Total = 0m, IsExempt = true }).Text
            .Should().Contain("LKR 0.00 (fee-exempt)");
    }

    [Theory]
    [InlineData(NotificationKinds.Rejected)]
    [InlineData(NotificationKinds.CancelledByOfficer)]
    public void An_officers_rejection_or_cancellation_carries_their_reason(string kind)
    {
        EmailTemplates.Render(Model(kind, reason: "The hall is reserved for exams")).Text
            .Should().Contain("Reason: The hall is reserved for exams");
    }

    [Theory]
    [InlineData(NotificationKinds.Closed)]
    [InlineData(NotificationKinds.RevisionRequested)]
    public void Closed_and_revision_emails_never_show_a_reason_or_notes(string kind)
    {
        var email = EmailTemplates.Render(Model(kind, reason: "The requested time is no longer valid: internal detail"));

        email.Text.Should().NotContain("internal detail").And.NotContain("Reason");
        email.Html.Should().NotContain("internal detail").And.NotContain("Reason");
    }

    [Fact]
    public void Revision_requested_says_the_request_is_being_re_planned()
    {
        var email = EmailTemplates.Render(Model(NotificationKinds.RevisionRequested));
        email.Subject.Should().StartWith("Booking request being re-planned");
        email.Text.Should().Contain("A new proposal is being prepared");
    }

    [Theory]
    [InlineData(NotificationKinds.Approved)]
    [InlineData(NotificationKinds.Rejected)]
    [InlineData(NotificationKinds.Closed)]
    [InlineData(NotificationKinds.RevisionRequested)]
    [InlineData(NotificationKinds.CancelledByOfficer)]
    public void No_body_carries_an_id_a_role_or_agent_wording(string kind)
    {
        var email = EmailTemplates.Render(Model(kind, reason: "Clash with exams"));

        foreach (var body in new[] { email.Subject, email.Html, email.Text })
        {
            foreach (var word in new[] { "Student", "Lecturer", "FacilitiesOfficer", "agent", "Request #", "booking-", " id", "Notes" })
                body.Should().NotContainEquivalentOf(word);
        }
    }

    [Fact]
    public void A_slot_across_midnight_shows_both_dates()
    {
        EmailTemplates.FormatSlot(new DateTimeOffset(2026, 11, 9, 22, 0, 0, CampusTime.Offset),
                new DateTimeOffset(2026, 11, 10, 1, 0, 0, CampusTime.Offset))
            .Should().Be("Mon 9 Nov 2026 22:00 – Tue 10 Nov 2026 01:00 (campus time, UTC+05:30)");
    }
}
