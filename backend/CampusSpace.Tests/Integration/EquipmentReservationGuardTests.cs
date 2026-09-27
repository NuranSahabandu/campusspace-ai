using System.Data;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.BookingTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// IEquipmentAvailabilityService.ReserveAsync, called the way the Phase 3 approval transaction will: a new booking and
/// its reservations added in one READ COMMITTED transaction, then one SaveChanges and a commit. Each test uses its own
/// equipment types and rooms.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EquipmentReservationGuardTests(PostgresFixture fixture)
{
    private static readonly DateOnly Day = new(2031, 3, 11);

    private CustomWebApplicationFactory Factory => fixture.Factory;

    private async Task<long> TypeAsync(int available, string? code = null)
    {
        var type = await EquipmentTestData.CreateTypeAsync(Factory, code);
        for (var i = 0; i < available; i++)
            await EquipmentTestData.CreateItemAsync(Factory, type.Id);
        return type.Id;
    }

    private async Task<long> RoomAsync()
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(Factory, prefix);
        return await FacilitiesTestData.CreateRoomAsync(Factory, buildingId, prefix + "-R", RoomTypes.SeminarRoom, 30, []);
    }

    /// <summary>An unsaved booking for a new approved request, as the approval transaction would build it.</summary>
    private async Task<Booking> NewBookingAsync(string from = "14:00", string to = "17:00")
    {
        var (start, end) = CampusSlot(Day, from, to);
        var requestId = await CreateApprovedRequestAsync(Factory, start, end);
        return new Booking { RequestId = requestId, RoomId = await RoomAsync(), TimeRange = CampusTime.UtcRange(start, end) };
    }

    private async Task<List<EquipmentReservation>> ReservationsOfAsync(params long[] typeIds)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().EquipmentReservations.AsNoTracking()
            .Where(r => typeIds.Contains(r.TypeId)).ToListAsync();
    }

    /// <summary>Adds the booking, reserves, saves and commits in one transaction; rolls back if ReserveAsync throws.</summary>
    private async Task ApproveAsync(Booking booking, params ReservationLine[] lines)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IEquipmentAvailabilityService>();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        db.Bookings.Add(booking);
        try
        {
            await guard.ReserveAsync(booking, lines);
        }
        catch
        {
            db.ChangeTracker.Entries<EquipmentReservation>().Should().BeEmpty("a failed guard adds nothing");
            throw;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Adds_one_row_per_type_with_the_bookings_range_summing_repeats_and_skipping_zero_lines()
    {
        var mics = await TypeAsync(available: 7);
        var laptops = await TypeAsync(available: 2);
        var builtIn = await TypeAsync(available: 0);
        var booking = await NewBookingAsync();

        await ApproveAsync(booking, new(mics, 2), new(laptops, 2), new(mics, 1), new(builtIn, 0));

        var rows = await ReservationsOfAsync(mics, laptops, builtIn);
        rows.Should().HaveCount(2).And.OnlyContain(r => r.BookingId == booking.Id && r.TimeRange == booking.TimeRange);
        rows.Single(r => r.TypeId == mics).Quantity.Should().Be(3);
        rows.Single(r => r.TypeId == laptops).Quantity.Should().Be(2);
    }

    [Fact]
    public async Task A_short_line_is_a_409_naming_the_type_and_what_is_available_and_adds_nothing()
    {
        var code = EquipmentTestData.UniqueTypeCode();
        var typeId = await TypeAsync(available: 7, code);
        await ApproveAsync(await NewBookingAsync("13:00", "15:00"), new ReservationLine(typeId, 5));

        var act = async () => await ApproveAsync(await NewBookingAsync("14:00", "17:00"), new ReservationLine(typeId, 3));

        (await act.Should().ThrowAsync<ConflictException>()).Which.Message
            .Should().Be($"Not enough equipment: {code} (requested 3, available 2)");
        (await ReservationsOfAsync(typeId)).Should().ContainSingle();
    }

    [Fact]
    public async Task With_several_lines_every_short_one_is_listed_and_none_are_added()
    {
        var plentyCode = EquipmentTestData.UniqueTypeCode();
        var shortCode = EquipmentTestData.UniqueTypeCode();
        var noneCode = EquipmentTestData.UniqueTypeCode();
        var plenty = await TypeAsync(available: 5, plentyCode);
        var few = await TypeAsync(available: 1, shortCode);
        var none = await TypeAsync(available: 0, noneCode);

        var act = async () => await ApproveAsync(await NewBookingAsync(), new(plenty, 2), new(few, 2), new(none, 1));

        var message = (await act.Should().ThrowAsync<ConflictException>()).Which.Message;
        message.Should().Contain($"{shortCode} (requested 2, available 1)").And.Contain($"{noneCode} (requested 1, available 0)")
            .And.NotContain(plentyCode);
        (await ReservationsOfAsync(plenty, few, none)).Should().BeEmpty();
    }

    [Fact]
    public async Task Needs_a_transaction_and_it_must_be_read_committed()
    {
        var typeId = await TypeAsync(available: 1);
        var booking = await NewBookingAsync();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var guard = scope.ServiceProvider.GetRequiredService<IEquipmentAvailabilityService>();

        await guard.Invoking(g => g.ReserveAsync(booking, [new(typeId, 1)]))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*inside the approval transaction*");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await guard.Invoking(g => g.ReserveAsync(booking, [new(typeId, 1)]))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*READ COMMITTED*");
        db.ChangeTracker.Entries<EquipmentReservation>().Should().BeEmpty();
    }

    [Fact]
    public async Task Of_two_parallel_approvals_taking_5_of_7_exactly_one_commits()
    {
        // A race only shows up sometimes, so run several rounds. Without the advisory lock some rounds commit both.
        for (var round = 0; round < 5; round++)
        {
            var typeId = await TypeAsync(available: 7);
            var bookings = new[] { await NewBookingAsync("14:00", "17:00"), await NewBookingAsync("15:00", "18:00") };

            var results = await Task.WhenAll(bookings.Select(async booking =>
            {
                try
                {
                    await ApproveAsync(booking, new ReservationLine(typeId, 5));
                    return "committed";
                }
                catch (ConflictException)
                {
                    return "409";
                }
            }));

            results.Should().BeEquivalentTo(["committed", "409"], $"round {round}");
            (await ReservationsOfAsync(typeId)).Sum(r => r.Quantity).Should().Be(5);
        }
    }
}
