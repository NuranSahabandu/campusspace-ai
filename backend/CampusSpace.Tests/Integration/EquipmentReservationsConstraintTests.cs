using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;
using static CampusSpace.Tests.Infrastructure.BookingTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The EquipmentReservations table (§8.1): CHECKs, the one-row-per-type UNIQUE index, CASCADE from Bookings and
/// RESTRICT to EquipmentTypes. Raw SQL inserts, so no application check can hide a missing constraint.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EquipmentReservationsConstraintTests(PostgresFixture fixture)
{
    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static readonly DateOnly Day = new(2031, 3, 13);

    private async Task<(long BookingId, long TypeId)> BookingAndTypeAsync()
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(Factory, prefix);
        var roomId = await FacilitiesTestData.CreateRoomAsync(Factory, buildingId, prefix + "-R", RoomTypes.SeminarRoom, 30, []);
        var (start, end) = CampusSlot(Day, "10:00", "12:00");
        var bookingId = await InsertBookingAsync(Factory, roomId, start, end);
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        return (bookingId, type.Id);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Inserts a reservation whose range is the literal <paramref name="range"/> (a tstzrange expression).</summary>
    private async Task InsertAsync(long bookingId, long typeId, int quantity, string range = "tstzrange('2031-03-13 04:30Z', '2031-03-13 06:30Z', '[)')")
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand($"""
            INSERT INTO "EquipmentReservations" ("BookingId", "TypeId", "Quantity", "TimeRange", "CreatedAt", "UpdatedAt")
            VALUES (@booking, @type, @quantity, {range}, now(), now())
            """, connection);
        command.Parameters.AddWithValue("booking", bookingId);
        command.Parameters.AddWithValue("type", typeId);
        command.Parameters.AddWithValue("quantity", quantity);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(long bookingId)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM \"EquipmentReservations\" WHERE \"BookingId\" = @booking", connection);
        command.Parameters.AddWithValue("booking", bookingId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ShouldFailAsync(Func<Task> act, string sqlState, string constraint)
    {
        var error = (await act.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(sqlState);
        error.ConstraintName.Should().Be(constraint);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Quantity_must_be_positive(int quantity)
    {
        var (bookingId, typeId) = await BookingAndTypeAsync();

        await ShouldFailAsync(() => InsertAsync(bookingId, typeId, quantity),
            PostgresErrorCodes.CheckViolation, "CK_EquipmentReservations_Quantity");
    }

    [Theory]
    [InlineData("'empty'::tstzrange")]
    [InlineData("tstzrange('2031-03-13 04:30Z', NULL, '[)')")]
    [InlineData("tstzrange('2031-03-13 04:30Z', '2031-03-13 06:30Z', '(]')")]
    public async Task Time_range_must_be_finite_non_empty_and_start_inclusive_end_exclusive(string range)
    {
        var (bookingId, typeId) = await BookingAndTypeAsync();

        await ShouldFailAsync(() => InsertAsync(bookingId, typeId, 1, range),
            PostgresErrorCodes.CheckViolation, "CK_EquipmentReservations_TimeRange");
    }

    [Fact]
    public async Task A_booking_has_at_most_one_row_per_type()
    {
        var (bookingId, typeId) = await BookingAndTypeAsync();
        await InsertAsync(bookingId, typeId, 2);

        await ShouldFailAsync(() => InsertAsync(bookingId, typeId, 1),
            PostgresErrorCodes.UniqueViolation, "IX_EquipmentReservations_BookingId_TypeId");
    }

    [Fact]
    public async Task Deleting_the_booking_deletes_its_reservations()
    {
        var (bookingId, typeId) = await BookingAndTypeAsync();
        await InsertAsync(bookingId, typeId, 2);

        await using var connection = await OpenAsync();
        await using var delete = new NpgsqlCommand("DELETE FROM \"Bookings\" WHERE \"Id\" = @id", connection);
        delete.Parameters.AddWithValue("id", bookingId);
        await delete.ExecuteNonQueryAsync();

        (await CountAsync(bookingId)).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_a_reserved_type_returns_409_in_use()
    {
        var (officer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);
        var (bookingId, typeId) = await BookingAndTypeAsync();
        await InsertAsync(bookingId, typeId, 1);

        var response = await officer.DeleteAsync($"/api/equipment-types/{typeId}");

        // The type has no items, so only the RESTRICT FK from EquipmentReservations stops the delete (23503).
        (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be("In use");
        (await CountAsync(bookingId)).Should().Be(1);
    }

    [Fact]
    public async Task Reservations_have_a_gist_index_on_type_and_time_range()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'EquipmentReservations' AND indexname = @name", connection);
        command.Parameters.AddWithValue("name", EquipmentReservationConfiguration.TypeTimeRangeIndex);

        var definition = (string?)await command.ExecuteScalarAsync();

        definition.Should().NotBeNull();
        definition.Should().Contain("USING gist").And.Contain("\"TypeId\", \"TimeRange\"");
    }
}
