using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.BookingRequestTestData;
using static CampusSpace.Tests.Infrastructure.BookingTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// POST /api/booking-requests/{id}/cancel (UC07). Every test uses fresh users and rooms. Approved bookings on the shared
/// database are in 2031, far outside free_cancellation_hours; the late flag and "already started" use a frozen clock.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BookingRequestsCancelTests(PostgresFixture fixture)
{
    private static readonly DateOnly Monday = new(2031, 3, 10);

    /// <summary>The frozen "now" of the late-flag tests: Wednesday 2031-03-12 10:00 campus time.</summary>
    private static readonly DateTimeOffset Now = CampusTime.At(new DateOnly(2031, 3, 12), new TimeOnly(10, 0));

    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static string CancelUrl(long id) => $"{Url}/{id}/cancel";

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, long id, string? reason = null) =>
        client.PostAsJsonAsync(CancelUrl(id), new { reason });

    /// <summary>Submits (the request is then AgentProcessing and its run Running).</summary>
    private static async Task<long> SubmitAsync(HttpClient client, long? clubId, DateTimeOffset? start = null)
    {
        var response = await client.PostAsJsonAsync(Url, Body(clubId, start: start));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt64();
    }

    /// <summary>Submits, then pauses the run for approval: the request is PendingApproval, so it can be cancelled.</summary>
    private async Task<long> PendingAsync(HttpClient client, long? clubId, DateTimeOffset? start = null)
    {
        var id = await SubmitAsync(client, clubId, start);
        await AgentRunTestData.ToPendingApprovalAsync(Factory, id);
        return id;
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadJsonAsync();
    }

    private static async Task<string?> TitleAsync(HttpResponseMessage response, int status) =>
        (await response.ShouldBeProblemAsync(status)).GetProperty("title").GetString();

    private static async Task<(long BuildingId, long RoomId)> RoomAsync(CustomWebApplicationFactory factory)
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(factory, prefix);
        return (buildingId, await FacilitiesTestData.CreateRoomAsync(factory, buildingId, prefix + "-R", RoomTypes.SeminarRoom, 30, []));
    }

    /// <summary>A lecturer and their Approved request with a Confirmed booking of a new room for [start, start + 2 h).</summary>
    private static async Task<(HttpClient Client, long UserId, long RequestId, long BookingId, long RoomId, long BuildingId)> ApprovedAsync(
        CustomWebApplicationFactory factory, DateTimeOffset start, string status = BookingStatuses.Confirmed)
    {
        var (client, userId) = await TestAuth.CreateUserClientAsync(factory, Roles.Lecturer);
        var (buildingId, roomId) = await RoomAsync(factory);
        var (requestId, bookingId) = await InsertApprovedBookingAsync(factory, roomId, start, start.AddHours(2), status, userId);
        return (client, userId, requestId, bookingId, roomId, buildingId);
    }

    private static async Task<T> QueryAsync<T>(CustomWebApplicationFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<string> QuoteStatusAsync(CustomWebApplicationFactory factory, long quotationId) =>
        QueryAsync(factory, db => db.Quotations.Where(q => q.Id == quotationId).Select(q => q.Status).SingleAsync());

    private static Task<string> BookingStatusAsync(CustomWebApplicationFactory factory, long bookingId) =>
        QueryAsync(factory, db => db.Bookings.Where(b => b.Id == bookingId).Select(b => b.Status).SingleAsync());

    private static Task IssueAsync(CustomWebApplicationFactory factory, long quotationId) =>
        QueryAsync(factory, db => db.Quotations.Where(q => q.Id == quotationId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QuotationStatuses.Issued)));

    private static void ShouldBeCancelled(JsonElement detail, bool late, bool byOfficer)
    {
        detail.GetProperty("status").GetString().Should().Be(RequestStatuses.Cancelled);
        detail.GetProperty("cancelledAt").ValueKind.Should().Be(JsonValueKind.String);
        detail.GetProperty("isLateCancellation").GetBoolean().Should().Be(late);
        detail.GetProperty("cancelledByOfficer").GetBoolean().Should().Be(byOfficer);
    }

    private static JsonElement LastHistory(JsonElement detail)
    {
        var history = detail.GetProperty("history");
        return history[history.GetArrayLength() - 1];
    }

    // ---- Who may cancel ----

    [Fact]
    public async Task The_owner_cancels_a_pending_request_without_a_body_and_frees_nothing_else()
    {
        var (client, userId, clubId) = await StudentRepAsync(Factory);
        var id = await PendingAsync(client, clubId);

        var response = await client.PostAsync(CancelUrl(id), content: null);

        var detail = await OkAsync(response);
        ShouldBeCancelled(detail, late: false, byOfficer: false);
        detail.GetProperty("cancelledAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        var last = LastHistory(detail);
        last.GetProperty("fromStatus").GetString().Should().Be(RequestStatuses.PendingApproval);
        last.GetProperty("toStatus").GetString().Should().Be(RequestStatuses.Cancelled);
        last.GetProperty("changedById").GetInt64().Should().Be(userId);
        last.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);

        // The list shows the same fields.
        var row = (await (await client.GetAsync(Url)).ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("id").GetInt64() == id);
        ShouldBeCancelled(row, late: false, byOfficer: false);
    }

    [Fact]
    public async Task A_lecturer_owner_cancels_a_pending_request_and_a_blank_reason_is_stored_as_null()
    {
        var (client, userId) = await TestAuth.CreateUserClientAsync(Factory, Roles.Lecturer);
        var id = await PendingAsync(client, clubId: null);

        var detail = await OkAsync(await CancelAsync(client, id, "   "));

        ShouldBeCancelled(detail, late: false, byOfficer: false);
        LastHistory(detail).GetProperty("fromStatus").GetString().Should().Be(RequestStatuses.PendingApproval);
        LastHistory(detail).GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        LastHistory(detail).GetProperty("changedById").GetInt64().Should().Be(userId);
    }

    [Fact]
    public async Task Other_requesters_lab_technicians_and_admins_are_forbidden_an_unknown_id_is_404_and_no_token_is_401()
    {
        var (owner, _, clubId) = await StudentRepAsync(Factory);
        var id = await PendingAsync(owner, clubId);
        var (other, _, _) = await StudentRepAsync(Factory);

        (await TitleAsync(await CancelAsync(other, id), 403)).Should().Be(BookingRequestService.CancelOwnMessage);
        foreach (var role in new[] { Roles.LabTechnician, Roles.Admin })
            await (await CancelAsync(TestAuth.CreateClient(Factory, role), id, "x")).ShouldBeProblemAsync(403);
        await (await CancelAsync(owner, long.MaxValue / 2)).ShouldBeProblemAsync(404);
        (await Factory.CreateClient().PostAsJsonAsync(CancelUrl(id), new { reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Nothing changed.
        (await (await owner.GetAsync($"{Url}/{id}")).ReadJsonAsync()).GetProperty("status").GetString()
            .Should().Be(RequestStatuses.PendingApproval);
    }

    [Fact]
    public async Task An_officer_must_give_a_reason()
    {
        var (owner, _, clubId) = await StudentRepAsync(Factory);
        var id = await PendingAsync(owner, clubId);
        var (officer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);

        foreach (var response in new[] { await officer.PostAsync(CancelUrl(id), null), await CancelAsync(officer, id, " \t ") })
            (await response.ShouldBeProblemAsync(400)).GetProperty("errors").GetProperty("Reason")[0].GetString()
                .Should().Be(BookingRequestService.OfficerReasonMessage);
    }

    [Fact]
    public async Task An_officer_cancels_a_pending_request_with_a_reason_and_is_never_late()
    {
        var (owner, _, clubId) = await StudentRepAsync(Factory);
        var id = await PendingAsync(owner, clubId);
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);

        var detail = await OkAsync(await CancelAsync(officer, id, "  Room closed for exams  "));

        ShouldBeCancelled(detail, late: false, byOfficer: true);
        LastHistory(detail).GetProperty("reason").GetString().Should().Be("Room closed for exams");
        LastHistory(detail).GetProperty("changedById").GetInt64().Should().Be(officerId);
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_a_400()
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var id = await PendingAsync(client, clubId);

        (await (await CancelAsync(client, id, new string('x', 501))).ShouldBeProblemAsync(400))
            .GetProperty("errors").TryGetProperty("Reason", out _).Should().BeTrue();
        await OkAsync(await CancelAsync(client, id, new string('x', 500)));
    }

    [Fact]
    public async Task A_reason_is_plain_text_stored_and_returned_unchanged()
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var id = await PendingAsync(client, clubId);
        const string reason = "<script>alert(1)</script> & <b>bold</b> \"quoted\"";

        await OkAsync(await CancelAsync(client, id, reason));

        var history = await (await client.GetAsync($"{Url}/{id}/history")).ReadJsonAsync();
        history[history.GetArrayLength() - 1].GetProperty("reason").GetString().Should().Be(reason);
        (await QueryAsync(Factory, db => db.RequestStatusHistory
                .Where(h => h.RequestId == id && h.ToStatus == RequestStatuses.Cancelled).Select(h => h.Reason).SingleAsync()))
            .Should().Be(reason);
    }

    // ---- Statuses ----

    // Paths start from AgentProcessing, where submit leaves a request (an empty path: cancelling it right away).
    [Theory]
    [InlineData(new string[0], BookingRequestService.ProcessingMessage)]
    [InlineData(new[] { RequestStatuses.AgentFailed }, BookingRequestService.AgentFailedMessage)]
    [InlineData(new[] { RequestStatuses.PendingApproval, RequestStatuses.RevisionRequested }, BookingRequestService.RevisionMessage)]
    [InlineData(new[] { RequestStatuses.PendingApproval, RequestStatuses.Rejected }, "The request is already rejected")]
    [InlineData(new[] { RequestStatuses.PendingApproval, RequestStatuses.Approved, RequestStatuses.Completed },
        "The request is already completed")]
    [InlineData(new[] { RequestStatuses.PendingApproval, RequestStatuses.Cancelled }, "The request is already cancelled")]
    public async Task A_status_the_state_machine_cannot_cancel_is_a_409_with_its_message(string[] path, string message)
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var id = await SubmitAsync(client, clubId);
        await MoveAsync(Factory, id, path);

        (await TitleAsync(await CancelAsync(client, id), 409)).Should().Be(message);
    }

    [Fact]
    public async Task Cancelling_voids_the_live_draft_quote_and_a_second_cancel_is_a_409()
    {
        var (client, _, id) = await QuotationTestData.RequestAsync(Factory);
        await AgentRunTestData.ToPendingApprovalAsync(Factory, id);
        var quotationId = await QuotationTestData.CreateDraftAsync(Factory, id, QuotationTestData.Quote());

        await OkAsync(await CancelAsync(client, id));

        (await QuoteStatusAsync(Factory, quotationId)).Should().Be(QuotationStatuses.Void);
        (await TitleAsync(await CancelAsync(client, id), 409)).Should().Be("The request is already cancelled");
    }

    [Fact]
    public async Task Cancelling_a_pending_request_cancels_its_paused_run_voids_the_draft_and_ends_the_thread()
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var id = await SubmitAsync(client, clubId);
        var runId = await AgentRunTestData.ToPendingApprovalAsync(Factory, id);
        var quotationId = await QuotationTestData.CreateDraftAsync(Factory, id, QuotationTestData.Quote());

        await OkAsync(await CancelAsync(client, id));

        var run = await QueryAsync(Factory, db => db.AgentRuns.AsNoTracking().SingleAsync(r => r.Id == runId));
        run.Status.Should().Be(AgentRunStatuses.Cancelled);
        run.CompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await QuoteStatusAsync(Factory, quotationId)).Should().Be(QuotationStatuses.Void);
        Factory.AgentClient.Calls.Should().Contain(("resume", runId, AgentDecisions.Cancel));
    }

    [Fact]
    public async Task A_pending_request_is_still_cancelled_when_the_agent_service_is_down()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        factory.AgentClient.Resume = (_, _, _, _) => Task.FromResult(FakeAgentClient.Unavailable<AgentWorkflowAccepted>());
        var (client, _, clubId) = await StudentRepAsync(factory);
        var response = await client.PostAsJsonAsync(Url, Body(clubId));
        var id = (await response.ReadJsonAsync()).GetProperty("id").GetInt64();
        var runId = await AgentRunTestData.ToPendingApprovalAsync(factory, id);

        var detail = await OkAsync(await CancelAsync(client, id));

        detail.GetProperty("status").GetString().Should().Be(RequestStatuses.Cancelled);
        (await QueryAsync(factory, db => db.AgentRuns.Where(r => r.Id == runId).Select(r => r.Status).SingleAsync()))
            .Should().Be(AgentRunStatuses.Cancelled);
        factory.AgentClient.Calls.Should().Contain(("resume", runId, AgentDecisions.Cancel));
    }

    [Fact]
    public async Task Cancelling_an_approved_request_does_not_touch_the_agent_service()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var (client, _, id, _, _, _) = await ApprovedAsync(factory, FutureStart(30));

        await OkAsync(await CancelAsync(client, id));

        factory.AgentClient.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_frees_an_open_request_slot()
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var ids = new List<long>();
        for (var i = 0; i < 3; i++)
            ids.Add(await PendingAsync(client, clubId, FutureStart(10 + i)));
        (await client.PostAsJsonAsync(Url, Body(clubId, start: FutureStart(20)))).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await OkAsync(await CancelAsync(client, ids[1]));

        (await client.PostAsJsonAsync(Url, Body(clubId, start: FutureStart(20)))).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Parallel_cancels_of_one_request_serialise_so_exactly_one_succeeds()
    {
        // Several rounds, so a missing row lock (both reading PendingApproval before either commits) shows up reliably.
        for (var round = 0; round < 5; round++)
        {
            var (client, _, clubId) = await StudentRepAsync(Factory);
            var id = await PendingAsync(client, clubId);

            var responses = await Task.WhenAll(CancelAsync(client, id), CancelAsync(client, id));

            responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
            var conflict = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
            (await TitleAsync(conflict, 409)).Should().Be("The request is already cancelled");
            (await QueryAsync(Factory, db => db.RequestStatusHistory.CountAsync(h => h.RequestId == id && h.ToStatus == RequestStatuses.Cancelled)))
                .Should().Be(1);
        }
    }

    // ---- Approved bookings ----

    [Fact]
    public async Task Cancelling_an_approved_request_releases_the_room_and_equipment_and_voids_the_issued_quote()
    {
        var start = CampusTime.At(Monday, new TimeOnly(14, 0));
        var (client, _, requestId, bookingId, roomId, buildingId) = await ApprovedAsync(Factory, start);
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        await EquipmentTestData.CreateItemAsync(Factory, type.Id);
        await EquipmentTestData.CreateItemAsync(Factory, type.Id);
        await EquipmentTestData.InsertReservationAsync(Factory, bookingId, type.Id, 2);
        var quotationId = await QuotationTestData.CreateDraftAsync(Factory, requestId, QuotationTestData.Quote());
        await IssueAsync(Factory, quotationId);

        var s = Uri.EscapeDataString(start.ToString("O"));
        var e = Uri.EscapeDataString(start.AddHours(2).ToString("O"));
        var reader = TestAuth.CreateClient(Factory, Roles.Student);
        async Task<bool> RoomListedAsync() =>
            (await (await reader.GetAsync($"/api/rooms/availability?start={s}&end={e}&minCapacity=1&buildingId={buildingId}")).ReadJsonAsync())
            .GetProperty("items").EnumerateArray().Any(r => r.GetProperty("id").GetInt64() == roomId);
        async Task<int> EquipmentAvailableAsync() =>
            (await (await reader.GetAsync($"/api/equipment/availability?typeId={type.Id}&start={s}&end={e}")).ReadJsonAsync())
            .GetProperty("available").GetInt32();
        (await RoomListedAsync()).Should().BeFalse();
        (await EquipmentAvailableAsync()).Should().Be(0);

        var detail = await OkAsync(await CancelAsync(client, requestId, "Event postponed"));

        ShouldBeCancelled(detail, late: false, byOfficer: false);
        LastHistory(detail).GetProperty("fromStatus").GetString().Should().Be(RequestStatuses.Approved);
        LastHistory(detail).GetProperty("reason").GetString().Should().Be("Event postponed");
        (await BookingStatusAsync(Factory, bookingId)).Should().Be(BookingStatuses.Cancelled);
        (await QuoteStatusAsync(Factory, quotationId)).Should().Be(QuotationStatuses.Void);

        // Shown through the real API: the room is listed again and the equipment is free.
        (await RoomListedAsync()).Should().BeTrue();
        (await EquipmentAvailableAsync()).Should().Be(2);
        // And no_room_overlap accepts a new booking of the same room and slot.
        await InsertBookingAsync(Factory, roomId, start, start.AddHours(2));
    }

    [Fact]
    public async Task An_approved_request_without_a_confirmed_booking_is_a_409_and_nothing_changes()
    {
        var start = CampusTime.At(Monday, new TimeOnly(9, 0));
        var (client, userId) = await TestAuth.CreateUserClientAsync(Factory, Roles.Lecturer);
        var requestId = await CreateApprovedRequestAsync(Factory, start, start.AddHours(2), userId);
        var quotationId = await QuotationTestData.CreateDraftAsync(Factory, requestId, QuotationTestData.Quote());

        (await TitleAsync(await CancelAsync(client, requestId), 409)).Should().Be(BookingRequestService.BookingNotCancellableMessage);

        (await QuoteStatusAsync(Factory, quotationId)).Should().Be(QuotationStatuses.Draft);
        var request = await QueryAsync(Factory, db => db.BookingRequests.AsNoTracking().SingleAsync(r => r.Id == requestId));
        request.Status.Should().Be(RequestStatuses.Approved);
        request.CancelledAt.Should().BeNull();
    }

    // ---- Equipment on loan (frozen clock: checkout opens before the start) ----

    [Fact]
    public async Task A_booking_with_equipment_on_loan_cannot_be_cancelled_until_it_is_checked_in()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Now));
        var owner = await ApprovedAsync(factory, Now.AddMinutes(20));
        var type = await EquipmentTestData.CreateTypeAsync(factory);
        var item = await EquipmentTestData.CreateItemAsync(factory, type.Id);
        await EquipmentTestData.InsertReservationAsync(factory, owner.BookingId, type.Id, 1);
        var (tech, _) = await LoanTestData.TechnicianAsync(factory);
        var loanId = await LoanTestData.CheckoutOkAsync(tech, owner.BookingId, item.Id);

        (await TitleAsync(await CancelAsync(owner.Client, owner.RequestId), 409)).Should().Be(BookingRequestService.EquipmentOnLoanMessage);
        (await BookingStatusAsync(factory, owner.BookingId)).Should().Be(BookingStatuses.Confirmed);

        (await LoanTestData.CheckInAsync(tech, loanId, EquipmentConditions.Good)).StatusCode.Should().Be(HttpStatusCode.OK);

        ShouldBeCancelled(await OkAsync(await CancelAsync(owner.Client, owner.RequestId)), late: true, byOfficer: false);
        (await BookingStatusAsync(factory, owner.BookingId)).Should().Be(BookingStatuses.Cancelled);
    }

    // ---- The late flag and started bookings (frozen clock) ----

    [Fact]
    public async Task An_owner_is_late_only_after_the_free_cancellation_boundary_and_an_officer_is_never_late()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Now));
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);

        // Seeded free_cancellation_hours is 24. Exactly on the boundary is free; one minute inside it is late.
        var onBoundary = await ApprovedAsync(factory, Now.AddHours(24));
        var justInside = await ApprovedAsync(factory, Now.AddHours(24).AddMinutes(-1));
        var byOfficer = await ApprovedAsync(factory, Now.AddHours(1));

        ShouldBeCancelled(await OkAsync(await CancelAsync(onBoundary.Client, onBoundary.RequestId)), late: false, byOfficer: false);
        var late = await OkAsync(await CancelAsync(justInside.Client, justInside.RequestId));
        ShouldBeCancelled(late, late: true, byOfficer: false);
        late.GetProperty("cancelledAt").GetDateTime().Should().Be(Now.UtcDateTime);
        ShouldBeCancelled(await OkAsync(await CancelAsync(officer, byOfficer.RequestId, "Blackout clash")), late: false, byOfficer: true);
    }

    [Fact]
    public async Task The_late_flag_uses_the_current_free_cancellation_hours()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Now));
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);
        var put = await officer.PutAsJsonAsync("/api/policy-settings",
            new { settings = new[] { new { key = PolicyKeys.FreeCancellationHours, value = "12" } } });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        // 13 h ahead would be late under the seeded 24 h; under 12 h it is free. 11 h ahead is late.
        var free = await ApprovedAsync(factory, Now.AddHours(13));
        var late = await ApprovedAsync(factory, Now.AddHours(11));

        ShouldBeCancelled(await OkAsync(await CancelAsync(free.Client, free.RequestId)), late: false, byOfficer: false);
        ShouldBeCancelled(await OkAsync(await CancelAsync(late.Client, late.RequestId)), late: true, byOfficer: false);
    }

    [Fact]
    public async Task A_booking_that_has_started_or_is_checked_in_cannot_be_cancelled()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Now));
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);

        var startsNow = await ApprovedAsync(factory, Now);
        var startedEarlier = await ApprovedAsync(factory, Now.AddHours(-1));
        var checkedIn = await ApprovedAsync(factory, Now.AddHours(5), BookingStatuses.CheckedIn);

        (await TitleAsync(await CancelAsync(startsNow.Client, startsNow.RequestId), 409)).Should().Be(BookingRequestService.BookingStartedMessage);
        (await TitleAsync(await CancelAsync(officer, startedEarlier.RequestId, "x"), 409)).Should().Be(BookingRequestService.BookingStartedMessage);
        (await TitleAsync(await CancelAsync(checkedIn.Client, checkedIn.RequestId), 409))
            .Should().Be(BookingRequestService.BookingNotCancellableMessage);
        (await BookingStatusAsync(factory, startsNow.BookingId)).Should().Be(BookingStatuses.Confirmed);
    }
}
