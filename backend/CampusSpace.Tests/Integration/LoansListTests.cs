using System.Net;
using System.Text.Json;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using static CampusSpace.Tests.Infrastructure.LoanTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// GET /api/loans/today (UC09) and GET /api/loans?overdue= (UC12). "Today" uses a frozen clock on its own database, so
/// the list holds only this test's bookings. The overdue tests run on the shared database and look only at their own loans.
/// </summary>
[Collection(PostgresCollection.Name)]
public class LoansListTests(PostgresFixture fixture)
{
    private static readonly DateOnly Today = new(2031, 3, 12);

    /// <summary>Wednesday 2031-03-12 10:00 campus time.</summary>
    private static readonly DateTimeOffset Now = CampusTime.At(Today, new TimeOnly(10, 0));

    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static DateTimeOffset At(DateOnly date, string time) => CampusTime.At(date, TimeOnly.Parse(time));

    private static async Task<List<JsonElement>> TodayAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/loans/today");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.ReadJsonAsync()).EnumerateArray().ToList();
    }

    [Fact]
    public async Task Today_lists_active_bookings_of_the_campus_date_with_reservations_and_their_counts()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Now));
        var (tech, techId) = await TechnicianAsync(factory);

        var morning = await HandoverAsync(factory, At(Today, "10:30"), At(Today, "12:30"), reserved: 3);
        var lateNight = await HandoverAsync(factory, At(Today, "23:00"), At(Today, "23:59"));
        var early = await HandoverAsync(factory, At(Today, "08:00"), At(Today, "09:00"), status: BookingStatuses.CheckedIn);
        // Not listed: cancelled, no reservation, ending exactly at today's campus midnight, starting at tomorrow's.
        await HandoverAsync(factory, At(Today, "13:00"), At(Today, "14:00"), status: BookingStatuses.Cancelled);
        await HandoverAsync(factory, At(Today, "15:00"), At(Today, "16:00"), reserved: 0);
        await HandoverAsync(factory, At(Today.AddDays(-1), "23:00"), CampusTime.StartOf(Today));
        await HandoverAsync(factory, CampusTime.StartOf(Today.AddDays(1)), At(Today.AddDays(1), "01:00"));

        // One out now, one out and returned.
        await CheckoutOkAsync(tech, morning.BookingId, morning.ItemIds[0]);
        await InsertLoanAsync(factory, morning.BookingId, morning.ItemIds[1], techId, Now, At(Today, "12:30"), checkedInAt: Now.AddMinutes(5));

        var list = await TodayAsync(tech);

        list.Select(b => b.GetProperty("bookingId").GetInt64()).Should().Equal(early.BookingId, morning.BookingId, lateNight.BookingId);
        var row = list[1];
        row.GetProperty("start").GetDateTime().Should().Be(At(Today, "10:30").UtcDateTime);
        row.GetProperty("end").GetDateTime().Should().Be(At(Today, "12:30").UtcDateTime);
        row.GetProperty("requesterName").GetString().Should().StartWith("Requester");
        row.GetProperty("roomCode").GetString().Should().EndWith("-R");
        row.TryGetProperty("purpose", out _).Should().BeFalse();
        var line = row.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("typeCode").GetString().Should().Be(morning.TypeCode);
        line.GetProperty("reserved").GetInt32().Should().Be(3);
        line.GetProperty("out").GetInt32().Should().Be(1);
        line.GetProperty("returned").GetInt32().Should().Be(1);
        list[0].GetProperty("status").GetString().Should().Be(BookingStatuses.CheckedIn);
    }

    [Fact]
    public async Task Today_follows_the_campus_date_not_the_utc_date()
    {
        // 00:10 on 2031-03-12 campus time is still 2031-03-11 in UTC.
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(At(Today, "00:10")));
        var (tech, _) = await TechnicianAsync(factory);
        var today = await HandoverAsync(factory, At(Today, "00:00"), At(Today, "01:00"));
        await HandoverAsync(factory, At(Today.AddDays(-1), "20:00"), At(Today.AddDays(-1), "23:59"));

        (await TodayAsync(tech)).Select(b => b.GetProperty("bookingId").GetInt64()).Should().Equal(today.BookingId);
    }

    [Fact]
    public async Task Today_is_for_technicians_and_officers()
    {
        var (officer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);

        (await officer.GetAsync("/api/loans/today")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestAuth.CreateClient(Factory, Roles.Student).GetAsync("/api/loans/today")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Factory.CreateClient().GetAsync("/api/loans/today")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Loans of one new type: overdue (due 2 h and 1 h ago), returned (was due 3 h ago), and not yet due.</summary>
    private async Task<(string TypeCode, long Overdue2h, long Overdue1h, long Returned, long NotDue)> LoansAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var h = await HandoverAsync(Factory, now.AddHours(-5), now.AddHours(-1), reserved: 3, items: 4);
        var (_, techId) = await TechnicianAsync(Factory);
        var returned = await InsertLoanAsync(Factory, h.BookingId, h.ItemIds[0], techId, now.AddHours(-5), now.AddHours(-3), now.AddHours(-3.5));
        var overdue2h = await InsertLoanAsync(Factory, h.BookingId, h.ItemIds[1], techId, now.AddHours(-5), now.AddHours(-2));
        var overdue1h = await InsertLoanAsync(Factory, h.BookingId, h.ItemIds[2], techId, now.AddHours(-5), now.AddHours(-1));
        var notDue = await InsertLoanAsync(Factory, h.BookingId, h.ItemIds[3], techId, now.AddHours(-5), now.AddHours(1));
        return (h.TypeCode, overdue2h, overdue1h, returned, notDue);
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/loans?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToList();
    }

    [Fact]
    public async Task Overdue_lists_open_loans_past_due_newest_due_first()
    {
        var loans = await LoansAsync();
        var (tech, _) = await TechnicianAsync(Factory);

        var overdue = await ListAsync(tech, $"overdue=true&search={loans.TypeCode}");

        overdue.Select(l => l.GetProperty("id").GetInt64()).Should().Equal(loans.Overdue1h, loans.Overdue2h);
        overdue.Should().OnlyContain(l => l.GetProperty("isOverdue").GetBoolean());
        overdue[0].GetProperty("typeCode").GetString().Should().Be(loans.TypeCode);
        overdue[0].GetProperty("assetTag").GetString().Should().StartWith("EQ-T-");
        overdue[0].GetProperty("checkedOutByName").GetString().Should().StartWith("Test LabTechnician");
    }

    [Fact]
    public async Task Not_overdue_lists_returned_and_not_yet_due_loans()
    {
        var loans = await LoansAsync();
        var (officer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);

        var rest = await ListAsync(officer, $"overdue=false&search={loans.TypeCode}");
        var all = await ListAsync(officer, $"search={loans.TypeCode}&sort=dueAt");

        rest.Select(l => l.GetProperty("id").GetInt64()).Should().Equal(loans.NotDue, loans.Returned);
        rest.Should().OnlyContain(l => !l.GetProperty("isOverdue").GetBoolean());
        all.Select(l => l.GetProperty("id").GetInt64()).Should().Equal(loans.Returned, loans.Overdue2h, loans.Overdue1h, loans.NotDue);
    }

    [Fact]
    public async Task The_loan_list_is_staff_only_and_validates_sort()
    {
        (await TestAuth.CreateClient(Factory, Roles.Student).GetAsync("/api/loans?overdue=true")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Factory.CreateClient().GetAsync("/api/loans?overdue=true")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var (tech, _) = await TechnicianAsync(Factory);
        (await tech.GetAsync("/api/loans?sort=assetTag")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
