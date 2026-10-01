using CampusSpace.Api.Models;
using CampusSpace.Api.Notifications;
using CampusSpace.Api.Services;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>Which status changes get an email, and which kind (Task 5.2).</summary>
public class NotificationKindTests
{
    private static RequestStatusHistory Change(string to, long? by = 5, string? reason = null) =>
        new() { ToStatus = to, ChangedById = by, Reason = reason };

    [Theory]
    [InlineData(RequestStatuses.Submitted)]
    [InlineData(RequestStatuses.AgentProcessing)]
    [InlineData(RequestStatuses.PendingApproval)]
    [InlineData(RequestStatuses.AgentFailed)]
    [InlineData(RequestStatuses.Completed)]
    public void Other_statuses_send_nothing(string to) => NotificationOutbox.KindFor(Change(to), false).Should().BeNull();

    [Fact]
    public void An_owners_cancel_sends_nothing_and_an_officers_cancel_does() =>
        (NotificationOutbox.KindFor(Change(RequestStatuses.Cancelled), false),
            NotificationOutbox.KindFor(Change(RequestStatuses.Cancelled), true))
        .Should().Be(((string?)null, (string?)NotificationKinds.CancelledByOfficer));

    [Fact]
    public void The_time_close_marker_means_Closed() =>
        NotificationOutbox.KindFor(Change(RequestStatuses.Rejected, by: null,
            reason: ApprovalFinalizer.TimeClosedReason("Start must be in the future")), false)
        .Should().Be(NotificationKinds.Closed);

    [Fact]
    public void An_officers_rejection_is_Rejected() =>
        NotificationOutbox.KindFor(Change(RequestStatuses.Rejected, by: 9, reason: "Exams"), false).Should().Be(NotificationKinds.Rejected);

    [Fact]
    public void A_system_rejection_without_the_marker_is_Closed_so_its_reason_is_never_sent() =>
        NotificationOutbox.KindFor(Change(RequestStatuses.Rejected, by: null, reason: "Some internal reason"), false)
            .Should().Be(NotificationKinds.Closed);

    [Theory]
    [InlineData(5L)]
    [InlineData(null)]
    public void Any_revision_requested_is_RevisionRequested(long? by) =>
        NotificationOutbox.KindFor(Change(RequestStatuses.RevisionRequested, by), false).Should().Be(NotificationKinds.RevisionRequested);
}
