using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;
using static CampusSpace.Tests.Infrastructure.BookingTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The no_room_overlap exclusion constraint (§8.2), tested with raw SQL inserts so no application check can hide a gap.
/// Each test uses rooms of its own, so the shared database's other bookings never overlap them.
/// </summary>
[Collection(PostgresCollection.Name)]
public class BookingsConstraintTests(PostgresFixture fixture)
{
    private const string Insert = """
        INSERT INTO "Bookings" ("RequestId", "RoomId", "TimeRange", "Status", "CreatedAt", "UpdatedAt")
        VALUES (@request, @room, tstzrange(@start, @end, '[)'), @status, now(), now())
        """;

    private CustomWebApplicationFactory Factory => fixture.Factory;

    /// <summary>A weekday far enough ahead that it never matters which day the tests run.</summary>
    private static readonly DateOnly Day = new(2031, 3, 12);

    private async Task<long> RoomAsync()
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(Factory, prefix);
        return await FacilitiesTestData.CreateRoomAsync(Factory, buildingId, prefix + "-R", RoomTypes.SeminarRoom, 30, []);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static NpgsqlCommand InsertCommand(
        NpgsqlConnection connection, long requestId, long roomId, string from, string to, string status, NpgsqlTransaction? tx = null)
    {
        var (start, end) = CampusSlot(Day, from, to);
        var command = new NpgsqlCommand(Insert, connection, tx);
        command.Parameters.AddWithValue("request", requestId);
        command.Parameters.AddWithValue("room", roomId);
        command.Parameters.AddWithValue("start", start.UtcDateTime);
        command.Parameters.AddWithValue("end", end.UtcDateTime);
        command.Parameters.AddWithValue("status", status);
        return command;
    }

    /// <summary>Inserts a booking for a new approved request, in its own implicit transaction.</summary>
    private async Task InsertAsync(long roomId, string from, string to, string status = BookingStatuses.Confirmed)
    {
        var (start, end) = CampusSlot(Day, from, to);
        var requestId = await CreateApprovedRequestAsync(Factory, start, end);
        await using var connection = await OpenAsync();
        await using var command = InsertCommand(connection, requestId, roomId, from, to, status);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ShouldBeRoomOverlapAsync(Func<Task> act)
    {
        var error = (await act.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.ExclusionViolation);
        error.ConstraintName.Should().Be(BookingConfiguration.NoRoomOverlapConstraint);
    }

    [Fact]
    public async Task The_constraint_exists_with_the_plan_definition()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = @name AND contype = 'x'", connection);
        command.Parameters.AddWithValue("name", BookingConfiguration.NoRoomOverlapConstraint);

        var definition = (string?)await command.ExecuteScalarAsync();

        definition.Should().NotBeNull();
        definition.Should().Contain("EXCLUDE USING gist").And.Contain("\"RoomId\" WITH =").And.Contain("\"TimeRange\" WITH &&")
            .And.Contain("'Confirmed'").And.Contain("'CheckedIn'").And.NotContain("'Cancelled'");
    }

    [Fact]
    public async Task Overlapping_confirmed_bookings_of_one_room_are_refused_with_23P01()
    {
        var room = await RoomAsync();
        await InsertAsync(room, "10:00", "12:00");

        await ShouldBeRoomOverlapAsync(() => InsertAsync(room, "11:30", "13:00"));
        await ShouldBeRoomOverlapAsync(() => InsertAsync(room, "09:00", "14:00"));
    }

    [Fact]
    public async Task Adjacent_ranges_are_allowed_because_the_end_is_exclusive()
    {
        var room = await RoomAsync();
        await InsertAsync(room, "10:00", "12:00");

        await InsertAsync(room, "12:00", "14:00");
        await InsertAsync(room, "08:00", "10:00");
    }

    [Fact]
    public async Task A_cancelled_or_completed_booking_does_not_hold_the_room()
    {
        var room = await RoomAsync();
        await InsertAsync(room, "10:00", "12:00", BookingStatuses.Cancelled);
        await InsertAsync(room, "10:00", "12:00", BookingStatuses.Completed);

        await InsertAsync(room, "10:00", "12:00");
    }

    [Fact]
    public async Task Different_rooms_can_be_booked_at_the_same_time()
    {
        var first = await RoomAsync();
        var second = await RoomAsync();

        await InsertAsync(first, "10:00", "12:00");
        await InsertAsync(second, "10:00", "12:00");
    }

    [Fact]
    public async Task A_checked_in_booking_counts_as_active()
    {
        var room = await RoomAsync();
        await InsertAsync(room, "10:00", "12:00", BookingStatuses.CheckedIn);

        await ShouldBeRoomOverlapAsync(() => InsertAsync(room, "11:00", "12:30"));
    }

    [Fact]
    public async Task A_request_has_at_most_one_booking()
    {
        var room = await RoomAsync();
        var (start, end) = CampusSlot(Day, "10:00", "11:00");
        var requestId = await CreateApprovedRequestAsync(Factory, start, end);
        await using var connection = await OpenAsync();
        await using (var first = InsertCommand(connection, requestId, room, "10:00", "11:00", BookingStatuses.Cancelled))
            await first.ExecuteNonQueryAsync();

        await using var second = InsertCommand(connection, requestId, room, "10:00", "11:00", BookingStatuses.Confirmed);
        var error = (await second.Invoking(c => c.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Of_two_concurrent_transactions_booking_the_same_slot_exactly_one_commits()
    {
        var room = await RoomAsync();
        var (start, end) = CampusSlot(Day, "14:00", "17:00");
        var requestA = await CreateApprovedRequestAsync(Factory, start, end);
        var requestB = await CreateApprovedRequestAsync(Factory, start, end);

        // Two officers approving clashing proposals: both transactions are open before either commits.
        await using var a = await OpenAsync();
        await using var b = await OpenAsync();
        await using var txA = await a.BeginTransactionAsync();
        await using var txB = await b.BeginTransactionAsync();
        await using (var insertA = InsertCommand(a, requestA, room, "14:00", "17:00", BookingStatuses.Confirmed, txA))
            await insertA.ExecuteNonQueryAsync();

        // B's insert waits on A's uncommitted row; it fails as soon as A commits.
        await using var insertB = InsertCommand(b, requestB, room, "15:00", "16:00", BookingStatuses.Confirmed, txB);
        var pending = insertB.ExecuteNonQueryAsync();
        await Task.Delay(200);
        pending.IsCompleted.Should().BeFalse("B must wait for A's transaction to finish");
        await txA.CommitAsync();

        await ShouldBeRoomOverlapAsync(() => pending);
        await txB.RollbackAsync();

        await using var count = new NpgsqlCommand("SELECT count(*) FROM \"Bookings\" WHERE \"RoomId\" = @room", a);
        count.Parameters.AddWithValue("room", room);
        (await count.ExecuteScalarAsync()).Should().Be(1L);
    }

    [Fact]
    public async Task Parallel_inserts_of_overlapping_bookings_give_exactly_one_success()
    {
        var room = await RoomAsync();
        var (start, end) = CampusSlot(Day, "08:00", "20:00");
        var requests = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CreateApprovedRequestAsync(Factory, start, end)));

        var results = await Task.WhenAll(requests.Select(async requestId =>
        {
            await using var connection = await OpenAsync();
            await using var command = InsertCommand(connection, requestId, room, "09:00", "11:00", BookingStatuses.Confirmed);
            try
            {
                await command.ExecuteNonQueryAsync();
                return "ok";
            }
            catch (PostgresException e)
            {
                return e.SqlState;
            }
        }));

        results.Should().ContainSingle(r => r == "ok");
        results.Where(r => r != "ok").Should().OnlyContain(r => r == PostgresErrorCodes.ExclusionViolation);
    }
}
