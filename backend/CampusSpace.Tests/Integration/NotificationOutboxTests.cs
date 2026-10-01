using System.Net;
using System.Net.Http.Json;
using System.Text;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Notifications;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CampusSpace.Tests.Infrastructure.BookingRequestTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The email outbox (Task 5.2): every status change that deserves an email gets exactly one Pending NotificationLog in the
/// same transaction, on every path that makes it, and the dispatcher sends it later without touching the decision.
/// </summary>
[Collection(PostgresCollection.Name)]
public class NotificationOutboxTests(PostgresFixture fixture)
{
    private const string RevisionNotes = "Please find a bigger room NOTES-MARKER";
    private const string RejectReason = "The lab is reserved for exams that week";

    private static async Task<(long RequestId, Guid RunId)> ApprovedSlowlyAsync(AgentPollerEnv env)
    {
        var (requestId, runId) = await env.ToPendingApprovalAsync();
        env.Agent.Get = (thread, _) => Task.FromResult(FakeAgentClient.Ok(FakeAgentClient.RunningView(thread)));
        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (requestId, runId);
    }

    private static void ShouldBeOnePending(List<NotificationLog> logs, string kind, string toStatus, string recipient)
    {
        var log = logs.Should().ContainSingle(n => n.Kind == kind).Subject;
        (log.Status, log.Channel, log.Recipient, log.Attempts, log.StatusHistory.ToStatus, log.SentAt, log.Error)
            .Should().Be((NotificationStatuses.Pending, NotificationChannels.Email, recipient, 0, toStatus, (DateTime?)null, (string?)null));
    }

    // ---------- enqueue: one row per path ----------

    [Fact]
    public async Task A_synchronous_approve_enqueues_exactly_one_pending_Approved_email_for_the_requester()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();

        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await env.NotificationsAsync(requestId);
        logs.Should().ContainSingle();
        ShouldBeOnePending(logs, NotificationKinds.Approved, RequestStatuses.Approved, await env.RequesterEmailAsync(requestId));
    }

    [Fact]
    public async Task An_approval_finished_by_the_poller_enqueues_the_Approved_email_and_the_dispatcher_sends_it_with_the_ics()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await ApprovedSlowlyAsync(env);
        (await env.NotificationsAsync(requestId)).Should().BeEmpty("nothing is approved yet");
        env.AgentReturns(AgentFixtures.View(AgentFixtures.CompletedStudent));
        await env.Poller.PollOnceAsync();
        await env.Poller.PollOnceAsync();

        ShouldBeOnePending(await env.NotificationsAsync(requestId), NotificationKinds.Approved, RequestStatuses.Approved,
            await env.RequesterEmailAsync(requestId));

        var sender = new FakeEmailSender();
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);
        (await email.Dispatcher().ProcessOnceAsync()).Should().Be(1);

        var log = (await env.NotificationsAsync(requestId)).Single();
        (log.Status, log.Attempts, log.ProviderMessageId, log.Error, log.RedirectedTo)
            .Should().Be((NotificationStatuses.Sent, 1, "<fake@brevo.test>", (string?)null, (string?)null));
        log.SentAt.Should().NotBeNull();
        var message = sender.Messages.Single();
        message.To.Should().Be(await env.RequesterEmailAsync(requestId));
        message.Subject.Should().Be("Booking confirmed: Robotics workshop");
        message.Text.Should().Contain("Computer Lab A301, Main Building").And.Contain("LKR 5,500.00");
        var booking = await env.QueryAsync(db => db.Bookings.AsNoTracking().SingleAsync(b => b.RequestId == requestId));
        var ics = Encoding.UTF8.GetString(message.Attachment!.Content);
        message.Attachment.Name.Should().Be("booking.ics");
        ics.Should().Contain($"UID:booking-{booking.Id}@campusspace.local\r\n")
            .And.Contain($"DTSTART:{booking.TimeRange.LowerBound:yyyyMMdd'T'HHmmss'Z'}\r\n")
            .And.Contain($"DTEND:{booking.TimeRange.UpperBound:yyyyMMdd'T'HHmmss'Z'}\r\n");

        (await email.Dispatcher().ProcessOnceAsync()).Should().Be(0, "a sent email is never sent again");
    }

    [Fact]
    public async Task A_rolled_back_approval_enqueues_no_Approved_email_only_the_re_plan_one()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        var (_, officerId) = await env.OfficerAsync();
        var request = await env.RequestAsync(requestId);
        var roomId = await env.QueryAsync(db => db.Rooms.Where(r => r.Code == "A301").Select(r => r.Id).SingleAsync());
        await env.QueryAsync(async db =>
        {
            db.RoomBlackouts.Add(new RoomBlackout
            {
                RoomId = roomId, Reason = "AC maintenance", CreatedById = officerId,
                TimeRange = CampusTime.UtcRange(AgentTrace.Utc(request.RequestedStart), AgentTrace.Utc(request.RequestedStart).AddHours(1)),
            });
            return await db.SaveChangesAsync();
        });

        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var logs = await env.NotificationsAsync(requestId);
        logs.Should().NotContain(n => n.Kind == NotificationKinds.Approved);
        var replan = logs.Should().ContainSingle().Subject;
        (replan.Kind, replan.StatusHistory.ChangedById).Should().Be((NotificationKinds.RevisionRequested, (long?)null));
    }

    [Fact]
    public async Task A_time_close_at_a_synchronous_approve_enqueues_a_Closed_email_without_the_reason()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        var start = (await env.RequestAsync(requestId)).RequestedStart;
        env.Clock.Set(new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).AddHours(1));

        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var logs = await env.NotificationsAsync(requestId);
        ShouldBeOnePending(logs, NotificationKinds.Closed, RequestStatuses.Rejected, await env.RequesterEmailAsync(requestId));
        logs.Should().ContainSingle();

        var sender = new FakeEmailSender();
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);
        await email.Dispatcher().ProcessOnceAsync();
        var message = sender.Messages.Single();
        message.Subject.Should().StartWith("Booking request closed");
        message.Text.Should().NotContain("Start must be in the future").And.NotContain("no longer valid").And.NotContain("Reason");
        message.Attachment.Should().BeNull();
    }

    [Fact]
    public async Task A_time_close_by_the_watchdog_enqueues_a_Closed_email()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await ApprovedSlowlyAsync(env);
        var start = (await env.RequestAsync(requestId)).RequestedStart;
        // The agent never confirms, and by the time the watchdog gives up the start has passed.
        env.Clock.Set(new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).AddMinutes(30));

        await env.Poller.PollOnceAsync();

        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.Rejected);
        var log = (await env.NotificationsAsync(requestId)).Should().ContainSingle().Subject;
        (log.Kind, log.StatusHistory.ChangedById).Should().Be((NotificationKinds.Closed, (long?)null));
    }

    [Fact]
    public async Task A_failed_finalize_seen_by_the_poller_enqueues_one_re_plan_email()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await ApprovedSlowlyAsync(env);
        env.AgentReturns(AgentFixtures.View(AgentFixtures.FinalizeFailed));

        await env.Poller.PollOnceAsync();

        var log = (await env.NotificationsAsync(requestId)).Should().ContainSingle().Subject;
        log.Kind.Should().Be(NotificationKinds.RevisionRequested);
    }

    [Fact]
    public async Task An_officers_rejection_enqueues_a_Rejected_email_that_carries_the_reason()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();

        (await env.DecideAsync(requestId, "reject", new { reason = RejectReason })).StatusCode.Should().Be(HttpStatusCode.OK);

        ShouldBeOnePending(await env.NotificationsAsync(requestId), NotificationKinds.Rejected, RequestStatuses.Rejected,
            await env.RequesterEmailAsync(requestId));
        var sender = new FakeEmailSender();
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);
        await email.Dispatcher().ProcessOnceAsync();
        var message = sender.Messages.Single();
        message.Text.Should().Contain($"Reason: {RejectReason}");
        message.Attachment.Should().BeNull();
    }

    [Fact]
    public async Task An_officers_revise_enqueues_a_re_plan_email_without_the_notes()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();

        (await env.DecideAsync(requestId, "request-revision", new { notes = RevisionNotes })).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var log = (await env.NotificationsAsync(requestId)).Should().ContainSingle().Subject;
        (log.Kind, log.StatusHistory.ToStatus).Should().Be((NotificationKinds.RevisionRequested, RequestStatuses.RevisionRequested));
        var sender = new FakeEmailSender();
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);
        await email.Dispatcher().ProcessOnceAsync();
        var message = sender.Messages.Single();
        message.Text.Should().NotContain("NOTES-MARKER").And.Contain("A new proposal is being prepared");
        message.Html.Should().NotContain("NOTES-MARKER");
        message.Attachment.Should().BeNull();
    }

    [Fact]
    public async Task An_officers_cancel_enqueues_a_CancelledByOfficer_email_and_an_owners_cancel_enqueues_none()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (approvedId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(approvedId, "approve")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await env.DecideAsync(approvedId, "cancel", new { reason = "Building closed for repairs" })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await env.NotificationsAsync(approvedId)).Select(n => n.Kind)
            .Should().Equal(NotificationKinds.Approved, NotificationKinds.CancelledByOfficer);

        var (owner, ownerId, _) = await StudentRepAsync(env.Factory);
        var ownId = await InsertSubmittedAsync(env.Factory, ownerId);
        (await owner.PostAsJsonAsync($"{Url}/{ownId}/cancel", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await env.NotificationsAsync(ownId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_agent_failure_and_retry_agent_enqueue_nothing()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.SubmitAsync();
        await AgentRunTestData.ToAgentFailedAsync(env.Factory, requestId);
        var (officer, _) = await env.OfficerAsync();

        (await officer.PostAsync($"{Url}/{requestId}/retry-agent", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await env.NotificationsAsync(requestId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_system_rejection_from_another_path_is_Closed_and_its_reason_is_never_sent()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (_, ownerId, _) = await StudentRepAsync(env.Factory);
        var requestId = await InsertSubmittedAsync(env.Factory, ownerId);

        // MoveAsync writes every step with no actor and the reason "test" (not the time-close marker).
        await MoveAsync(env.Factory, requestId, RequestStatuses.AgentProcessing, RequestStatuses.PendingApproval, RequestStatuses.Rejected);

        var log = (await env.NotificationsAsync(requestId)).Should().ContainSingle().Subject;
        (log.Kind, log.StatusHistory.Reason).Should().Be((NotificationKinds.Closed, "test"));
        var sender = new FakeEmailSender();
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);
        await email.Dispatcher().ProcessOnceAsync();
        sender.Messages.Single().Text.Should().NotContain("Reason");
    }

    [Fact]
    public async Task A_requester_created_in_the_same_save_gets_the_email_address_from_the_tracked_user()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var start = FutureStart(10);

        var requestId = await BookingTestData.CreateApprovedRequestAsync(env.Factory, start, start.AddHours(2));

        ShouldBeOnePending(await env.NotificationsAsync(requestId), NotificationKinds.Approved, RequestStatuses.Approved,
            await env.RequesterEmailAsync(requestId));
    }

    [Fact]
    public async Task One_status_change_can_have_only_one_email()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.OK);
        var log = (await env.NotificationsAsync(requestId)).Single();

        var act = () => env.QueryAsync(db =>
        {
            db.NotificationLogs.Add(new NotificationLog
            {
                RequestId = requestId, StatusHistoryId = log.StatusHistoryId, Kind = log.Kind, Recipient = log.Recipient,
            });
            return db.SaveChangesAsync();
        });

        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    // ---------- dispatch ----------

    [Fact]
    public async Task Without_a_key_the_email_is_Skipped_and_brevo_is_never_called()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await env.Factory.Dispatcher().ProcessOnceAsync()).Should().Be(1);

        var log = (await env.NotificationsAsync(requestId)).Single();
        (log.Status, log.Error, log.SentAt, log.RedirectedTo)
            .Should().Be((NotificationStatuses.Skipped, NoOpEmailSender.NotConfiguredMessage, (DateTime?)null, (string?)null));
    }

    [Fact]
    public async Task A_failed_send_is_final_and_leaves_the_approval_and_booking_untouched()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.OK);
        var sender = new FakeEmailSender { Respond = _ => EmailSendResult.Failed("HTTP 401 (unauthorized)") };
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);

        await email.Dispatcher().ProcessOnceAsync();
        await email.Dispatcher().ProcessOnceAsync();

        sender.Messages.Should().ContainSingle("Failed is final");
        var log = (await env.NotificationsAsync(requestId)).Single();
        (log.Status, log.Error, log.Attempts).Should().Be((NotificationStatuses.Failed, "HTTP 401 (unauthorized)", 1));
        (await env.RequestAsync(requestId)).Status.Should().Be(RequestStatuses.Approved);
        (await env.QueryAsync(db => db.Bookings.Where(b => b.RequestId == requestId).Select(b => b.Status).SingleAsync()))
            .Should().Be(BookingStatuses.Confirmed);
    }

    [Fact]
    public async Task The_redirect_sends_to_the_redirect_address_marks_the_subject_and_keeps_the_original_recipient()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "reject", new { reason = RejectReason })).StatusCode.Should().Be(HttpStatusCode.OK);
        var sender = new FakeEmailSender();
        await using var email = NotificationTestData.WithEmail(env.Factory, sender, redirect: "demo-inbox@campusspace.test");

        await email.Dispatcher().ProcessOnceAsync();

        var message = sender.Messages.Single();
        message.To.Should().Be("demo-inbox@campusspace.test");
        message.Subject.Should().StartWith(EmailTemplates.RedirectedPrefix + "Booking request not approved");
        var log = (await env.NotificationsAsync(requestId)).Single();
        (log.Status, log.Recipient, log.RedirectedTo)
            .Should().Be((NotificationStatuses.Sent, await env.RequesterEmailAsync(requestId), "demo-inbox@campusspace.test"));
    }

    [Fact]
    public async Task A_429_is_retried_after_its_Retry_After_and_given_up_after_three_attempts()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "reject", new { reason = RejectReason })).StatusCode.Should().Be(HttpStatusCode.OK);
        var sender = new FakeEmailSender { Respond = _ => EmailSendResult.Retry("HTTP 429 (too many requests)", TimeSpan.FromSeconds(42)) };
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);

        await email.Dispatcher().ProcessOnceAsync();
        var first = (await env.NotificationsAsync(requestId)).Single();
        (first.Status, first.Attempts, first.Error).Should().Be((NotificationStatuses.Pending, 1, "HTTP 429 (too many requests)"));
        first.NextAttemptAt.Should().BeCloseTo(env.Clock.GetUtcNow().UtcDateTime.AddSeconds(42), TimeSpan.FromSeconds(1));

        (await email.Dispatcher().ProcessOnceAsync()).Should().Be(0, "not due yet");
        env.Clock.Advance(TimeSpan.FromSeconds(43));
        (await email.Dispatcher().ProcessOnceAsync()).Should().Be(1);
        env.Clock.Advance(TimeSpan.FromSeconds(43));
        (await email.Dispatcher().ProcessOnceAsync()).Should().Be(1);
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        (await email.Dispatcher().ProcessOnceAsync()).Should().Be(0, "Failed is final");

        sender.Messages.Should().HaveCount(NotificationLogConfiguration.MaxAttempts);
        var last = (await env.NotificationsAsync(requestId)).Single();
        (last.Status, last.Attempts, last.Error, last.NextAttemptAt).Should().Be(
            (NotificationStatuses.Failed, 3, "Gave up after 3 attempts: HTTP 429 (too many requests)", (DateTime?)null));
    }

    [Fact]
    public async Task A_connection_failure_backs_off_30_seconds()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "reject", new { reason = RejectReason })).StatusCode.Should().Be(HttpStatusCode.OK);
        var sender = new FakeEmailSender { Respond = _ => EmailSendResult.Retry("Connection failed (ConnectionError)") };
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);

        await email.Dispatcher().ProcessOnceAsync();

        var log = (await env.NotificationsAsync(requestId)).Single();
        log.Status.Should().Be(NotificationStatuses.Pending);
        log.NextAttemptAt.Should().BeCloseTo(env.Clock.GetUtcNow().UtcDateTime.AddSeconds(30), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_row_stuck_in_Sending_is_Failed_and_never_sent_again()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "reject", new { reason = RejectReason })).StatusCode.Should().Be(HttpStatusCode.OK);
        var stuckAt = env.Clock.GetUtcNow().UtcDateTime;
        await env.QueryAsync(db => db.NotificationLogs.Where(n => n.RequestId == requestId).ExecuteUpdateAsync(s => s
            .SetProperty(n => n.Status, NotificationStatuses.Sending).SetProperty(n => n.Attempts, 1)
            .SetProperty(n => n.LastAttemptAt, stuckAt)));
        var sender = new FakeEmailSender();
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);

        await email.Dispatcher().ProcessOnceAsync();
        (await env.NotificationsAsync(requestId)).Single().Status.Should().Be(NotificationStatuses.Sending, "still inside 2 minutes");

        env.Clock.Advance(TimeSpan.FromMinutes(3));
        await email.Dispatcher().ProcessOnceAsync();

        sender.Messages.Should().BeEmpty();
        var log = (await env.NotificationsAsync(requestId)).Single();
        (log.Status, log.Error).Should().Be((NotificationStatuses.Failed, NotificationDispatcher.InterruptedMessage));
    }

    [Fact]
    public async Task An_approval_cancelled_before_its_email_went_out_is_Skipped()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await env.DecideAsync(requestId, "cancel", new { reason = "Changed plans" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var sender = new FakeEmailSender();
        await using var email = NotificationTestData.WithEmail(env.Factory, sender);

        await email.Dispatcher().ProcessOnceAsync();

        var logs = await env.NotificationsAsync(requestId);
        (logs[0].Kind, logs[0].Status, logs[0].Error)
            .Should().Be((NotificationKinds.Approved, NotificationStatuses.Skipped, NotificationDelivery.NoLongerApprovedMessage));
        (logs[1].Kind, logs[1].Status).Should().Be((NotificationKinds.CancelledByOfficer, NotificationStatuses.Sent));
        sender.Messages.Should().ContainSingle().Which.Subject.Should().StartWith("Booking request cancelled");
    }

    // ---------- GET /api/booking-requests/{id}/notifications ----------

    [Fact]
    public async Task Officers_read_a_requests_emails_requesters_get_403_and_an_unknown_request_404()
    {
        await using var env = await AgentPollerEnv.CreateAsync(fixture);
        var (requestId, _) = await env.ToPendingApprovalAsync();
        (await env.DecideAsync(requestId, "approve")).StatusCode.Should().Be(HttpStatusCode.OK);
        await env.Factory.Dispatcher().ProcessOnceAsync();
        var (officer, _) = await env.OfficerAsync();

        var response = await officer.GetAsync($"{Url}/{requestId}/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = (await response.ReadJsonAsync()).EnumerateArray().Single();
        item.GetProperty("kind").GetString().Should().Be(NotificationKinds.Approved);
        item.GetProperty("status").GetString().Should().Be(NotificationStatuses.Skipped);
        item.GetProperty("recipient").GetString().Should().Be(await env.RequesterEmailAsync(requestId));
        item.GetProperty("redirected").GetBoolean().Should().BeFalse();
        item.GetProperty("error").GetString().Should().Be(NoOpEmailSender.NotConfiguredMessage);

        var (student, _) = await TestAuth.CreateUserClientAsync(env.Factory, Roles.Student);
        (await student.GetAsync($"{Url}/{requestId}/notifications")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await officer.GetAsync($"{Url}/999999/notifications")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
