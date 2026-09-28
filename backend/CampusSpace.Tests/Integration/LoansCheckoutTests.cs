using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.LoanTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// POST /api/loans/checkout (UC10). Tests on the shared database book new rooms at real now + 10 min, so the checkout
/// window is open whatever the time of day. Window boundaries and policy changes use a frozen clock on their own database.
/// </summary>
[Collection(PostgresCollection.Name)]
public class LoansCheckoutTests(PostgresFixture fixture)
{
    /// <summary>The frozen "now" of the boundary tests: Wednesday 2031-03-12 10:00 campus time.</summary>
    private static readonly DateTimeOffset Now = CampusTime.At(new DateOnly(2031, 3, 12), new TimeOnly(10, 0));

    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static (DateTimeOffset Start, DateTimeOffset End) Soon()
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(10);
        return (start, start.AddHours(2));
    }

    private Task<Handover> SoonAsync(int reserved = 1, int? items = null, string status = BookingStatuses.Confirmed)
    {
        var (start, end) = Soon();
        return HandoverAsync(Factory, start, end, reserved, items, status);
    }

    private static async Task<string?> ConflictAsync(HttpResponseMessage response) =>
        (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString();

    [Fact]
    public async Task Checkout_creates_the_loan_due_at_the_booking_end_and_puts_the_item_on_loan()
    {
        var (start, end) = Soon();
        var h = await HandoverAsync(Factory, start, end);
        var (tech, techId) = await TechnicianAsync(Factory);

        var response = await CheckoutAsync(tech, h.BookingId, h.ItemIds[0]);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        var id = body.GetProperty("id").GetInt64();
        response.Headers.Location!.ToString().Should().EndWith($"/api/loans/{id}");
        body.GetProperty("typeCode").GetString().Should().Be(h.TypeCode);
        body.GetProperty("checkedInAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        var loan = await LoanAsync(Factory, id);
        loan.DueAt.Should().Be(end.UtcDateTime);
        loan.CheckedOutById.Should().Be(techId);
        loan.CheckedOutAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await ItemAsync(Factory, h.ItemIds[0])).Status.Should().Be(EquipmentItemStatuses.OnLoan);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AuditLogs.AnyAsync(a => a.EntityType == nameof(EquipmentLoan) && a.EntityId == id.ToString()
            && a.Action == AuditActions.Created && a.UserId == techId)).Should().BeTrue();
    }

    [Theory]
    [InlineData(EquipmentItemStatuses.UnderRepair, EquipmentConditions.Damaged, "Item is not available (UnderRepair)")]
    [InlineData(EquipmentItemStatuses.Retired, EquipmentConditions.Good, "Item is not available (Retired)")]
    public async Task An_item_that_is_not_available_is_refused(string status, string condition, string title)
    {
        var h = await SoonAsync();
        await SetItemAsync(Factory, h.ItemIds[0], status, condition);
        var (tech, _) = await TechnicianAsync(Factory);

        (await ConflictAsync(await CheckoutAsync(tech, h.BookingId, h.ItemIds[0]))).Should().Be(title);
    }

    [Fact]
    public async Task An_item_already_on_loan_is_refused()
    {
        var h = await SoonAsync(reserved: 2);
        var (tech, _) = await TechnicianAsync(Factory);
        await CheckoutOkAsync(tech, h.BookingId, h.ItemIds[0]);

        (await ConflictAsync(await CheckoutAsync(tech, h.BookingId, h.ItemIds[0])))
            .Should().Be(EquipmentLoanConfiguration.ItemOnLoanMessage);
    }

    [Fact]
    public async Task A_cancelled_booking_is_refused()
    {
        var h = await SoonAsync(status: BookingStatuses.Cancelled);
        var (tech, _) = await TechnicianAsync(Factory);

        (await ConflictAsync(await CheckoutAsync(tech, h.BookingId, h.ItemIds[0])))
            .Should().Be(LoanService.BookingNotActiveMessage(BookingStatuses.Cancelled));
        (await ItemAsync(Factory, h.ItemIds[0])).Status.Should().Be(EquipmentItemStatuses.Available);
    }

    [Fact]
    public async Task A_checked_in_booking_is_active_and_accepted()
    {
        var h = await SoonAsync(status: BookingStatuses.CheckedIn);
        var (tech, _) = await TechnicianAsync(Factory);

        (await CheckoutAsync(tech, h.BookingId, h.ItemIds[0])).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_type_not_reserved_for_the_booking_is_refused()
    {
        var h = await SoonAsync();
        var other = await EquipmentTestData.CreateTypeAsync(Factory);
        var item = await EquipmentTestData.CreateItemAsync(Factory, other.Id);
        var (tech, _) = await TechnicianAsync(Factory);

        (await ConflictAsync(await CheckoutAsync(tech, h.BookingId, item.Id)))
            .Should().Be(LoanService.NotReservedMessage(other.Code));
    }

    [Fact]
    public async Task No_more_than_the_reserved_quantity_goes_out()
    {
        var h = await SoonAsync(reserved: 2, items: 3);
        var (tech, _) = await TechnicianAsync(Factory);
        await CheckoutOkAsync(tech, h.BookingId, h.ItemIds[0]);
        await CheckoutOkAsync(tech, h.BookingId, h.ItemIds[1]);

        (await ConflictAsync(await CheckoutAsync(tech, h.BookingId, h.ItemIds[2])))
            .Should().Be(LoanService.AllOutMessage(2, h.TypeCode));
        (await ItemAsync(Factory, h.ItemIds[2])).Status.Should().Be(EquipmentItemStatuses.Available);
    }

    [Fact]
    public async Task A_returned_item_frees_its_unit_for_another_checkout()
    {
        var h = await SoonAsync(reserved: 1, items: 2);
        var (tech, _) = await TechnicianAsync(Factory);
        var loanId = await CheckoutOkAsync(tech, h.BookingId, h.ItemIds[0]);
        (await CheckInAsync(tech, loanId, EquipmentConditions.Good)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await CheckoutAsync(tech, h.BookingId, h.ItemIds[1])).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Unknown_booking_or_item_is_a_400_on_the_field()
    {
        var h = await SoonAsync();
        var (tech, _) = await TechnicianAsync(Factory);

        var noBooking = await (await CheckoutAsync(tech, long.MaxValue / 2, h.ItemIds[0])).ShouldBeProblemAsync(400);
        var noItem = await (await CheckoutAsync(tech, h.BookingId, long.MaxValue / 2)).ShouldBeProblemAsync(400);
        var zero = await (await CheckoutAsync(tech, 0, 0)).ShouldBeProblemAsync(400);

        noBooking.GetProperty("errors").GetProperty("BookingId")[0].GetString().Should().Be(LoanService.BookingMissingMessage);
        noItem.GetProperty("errors").GetProperty("ItemId")[0].GetString().Should().Be(LoanService.ItemMissingMessage);
        zero.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("BookingId", "ItemId");
    }

    [Theory]
    [InlineData(31, 120, HttpStatusCode.Conflict)]  // one minute too early
    [InlineData(30, 120, HttpStatusCode.Created)]   // exactly at the window start
    [InlineData(-60, 60, HttpStatusCode.Created)]   // during the booking
    [InlineData(-120, 0, HttpStatusCode.Conflict)]  // exactly at the end
    [InlineData(-180, -60, HttpStatusCode.Conflict)] // after the end
    public async Task Checkout_is_open_from_the_window_before_the_start_until_the_end(
        int startInMinutes, int endInMinutes, HttpStatusCode expected)
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Now));
        var h = await HandoverAsync(factory, Now.AddMinutes(startInMinutes), Now.AddMinutes(endInMinutes));
        var (tech, _) = await TechnicianAsync(factory);

        var response = await CheckoutAsync(tech, h.BookingId, h.ItemIds[0]);

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        if (expected != HttpStatusCode.Conflict)
            return;
        var title = (await response.ReadJsonAsync()).GetProperty("title").GetString();
        title.Should().Be(startInMinutes > 0 ? LoanService.TooEarlyMessage(Now.AddMinutes(1).UtcDateTime, 30) : LoanService.BookingEndedMessage);
        if (startInMinutes > 0)
            title.Should().Be("Checkout opens at 10:01 (30 minutes before the start)");
    }

    [Fact]
    public async Task Changing_checkout_window_minutes_through_the_policy_api_changes_the_checkout_result()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Now));
        var h = await HandoverAsync(factory, Now.AddMinutes(45), Now.AddMinutes(165));
        var (tech, _) = await TechnicianAsync(factory);
        var (officer, _) = await TestAuth.CreateUserClientAsync(factory, Roles.FacilitiesOfficer);

        (await CheckoutAsync(tech, h.BookingId, h.ItemIds[0])).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await officer.PutAsJsonAsync("/api/policy-settings",
            new { settings = new[] { new { key = PolicyKeys.CheckoutWindowMinutes, value = "60" } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await CheckoutAsync(tech, h.BookingId, h.ItemIds[0])).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Two_parallel_checkouts_for_the_last_reserved_unit_give_one_201_and_one_409()
    {
        var h = await SoonAsync(reserved: 1, items: 2);
        var (tech, _) = await TechnicianAsync(Factory);

        var responses = await Task.WhenAll(
            CheckoutAsync(tech, h.BookingId, h.ItemIds[0]), CheckoutAsync(tech, h.BookingId, h.ItemIds[1]));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await ConflictAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict)))
            .Should().Be(LoanService.AllOutMessage(1, h.TypeCode));
        var statuses = new[] { (await ItemAsync(Factory, h.ItemIds[0])).Status, (await ItemAsync(Factory, h.ItemIds[1])).Status };
        statuses.Should().BeEquivalentTo([EquipmentItemStatuses.OnLoan, EquipmentItemStatuses.Available]);
    }

    [Fact]
    public async Task Two_parallel_checkouts_of_one_item_for_two_bookings_give_one_201_and_one_409()
    {
        var first = await SoonAsync();
        var second = await SoonAsync();
        // Reserve the first booking's type for the second booking too, so the same item qualifies for both.
        await EquipmentTestData.InsertReservationAsync(Factory, second.BookingId, first.TypeId, 1);
        var item = first.ItemIds[0];
        var (tech, _) = await TechnicianAsync(Factory);

        var responses = await Task.WhenAll(CheckoutAsync(tech, first.BookingId, item), CheckoutAsync(tech, second.BookingId, item));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await ConflictAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Conflict)))
            .Should().Be(EquipmentLoanConfiguration.ItemOnLoanMessage);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.EquipmentLoans.CountAsync(l => l.ItemId == item)).Should().Be(1);
    }

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Lecturer)]
    [InlineData(Roles.FacilitiesOfficer)]
    public async Task Only_lab_technicians_check_out(string role)
    {
        var h = await SoonAsync();
        var (client, _) = await TestAuth.CreateUserClientAsync(Factory, role);

        (await CheckoutAsync(client, h.BookingId, h.ItemIds[0])).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CheckoutAsync(Factory.CreateClient(), h.BookingId, h.ItemIds[0])).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ItemAsync(Factory, h.ItemIds[0])).Status.Should().Be(EquipmentItemStatuses.Available);
    }
}
