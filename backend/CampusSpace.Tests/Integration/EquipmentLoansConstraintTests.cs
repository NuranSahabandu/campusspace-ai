using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;
using static CampusSpace.Tests.Infrastructure.BookingTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The EquipmentLoans table (§8.1): its CHECKs and the partial UNIQUE index that keeps an item on one open loan. Raw SQL
/// inserts, so no application check can hide a missing constraint. Every test makes its own room, booking, type and item.
/// </summary>
[Collection(PostgresCollection.Name)]
public class EquipmentLoansConstraintTests(PostgresFixture fixture)
{
    private CustomWebApplicationFactory Factory => fixture.Factory;

    private const string Out = "'2031-03-13 04:00Z'";
    private const string Due = "'2031-03-13 06:30Z'";
    private const string Photo = "'0123456789abcdef0123456789abcdef.jpg'";

    private async Task<(long BookingId, long ItemId, long UserId)> SetupAsync()
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(Factory, prefix);
        var roomId = await FacilitiesTestData.CreateRoomAsync(Factory, buildingId, prefix + "-R", RoomTypes.SeminarRoom, 30, []);
        var (start, end) = CampusSlot(new DateOnly(2031, 3, 13), "10:00", "12:00");
        var bookingId = await InsertBookingAsync(Factory, roomId, start, end);
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        var item = await EquipmentTestData.CreateItemAsync(Factory, type.Id);
        var (_, userId) = await TestAuth.CreateUserClientAsync(Factory, Roles.LabTechnician);
        return (bookingId, item.Id, userId);
    }

    /// <summary>
    /// Inserts a loan checked out at <see cref="Out"/> and due at <see cref="Due"/>. <paramref name="checkIn"/> is SQL for
    /// (CheckedInAt, CheckedInById, ReturnCondition, DamageNote, DamagePhotoPath, IsLateReturn); @user is the technician.
    /// </summary>
    private async Task InsertAsync((long BookingId, long ItemId, long UserId) s, string checkIn = "NULL, NULL, NULL, NULL, NULL, false",
        string checkedOut = Out, string due = Due)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            INSERT INTO "EquipmentLoans" ("BookingId", "ItemId", "CheckedOutAt", "CheckedOutById", "DueAt",
                "CheckedInAt", "CheckedInById", "ReturnCondition", "DamageNote", "DamagePhotoPath", "IsLateReturn",
                "CreatedAt", "UpdatedAt")
            VALUES (@booking, @item, {checkedOut}, @user, {due}, {checkIn}, now(), now())
            """, connection);
        command.Parameters.AddWithValue("booking", s.BookingId);
        command.Parameters.AddWithValue("item", s.ItemId);
        command.Parameters.AddWithValue("user", s.UserId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ShouldFailAsync(Func<Task> act, string sqlState, string constraint)
    {
        var error = (await act.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(sqlState);
        error.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task Open_and_closed_loans_are_valid()
    {
        var s = await SetupAsync();

        await InsertAsync(s, $"'2031-03-13 05:00Z', @user, 'Good', NULL, NULL, false");
        await InsertAsync(s, $"'2031-03-13 06:00Z', @user, 'Damaged', 'Cracked', {Photo}, false");
        await InsertAsync(s);
    }

    [Fact]
    public async Task An_item_can_have_only_one_open_loan()
    {
        var s = await SetupAsync();
        await InsertAsync(s);

        await ShouldFailAsync(() => InsertAsync(s), PostgresErrorCodes.UniqueViolation, EquipmentLoanConfiguration.OneOpenLoanIndex);
    }

    [Theory]
    [InlineData("'2031-03-13 05:00Z', NULL, 'Good', NULL, NULL, false")]
    [InlineData("'2031-03-13 05:00Z', @user, NULL, NULL, NULL, false")]
    [InlineData("NULL, @user, 'Good', NULL, NULL, false")]
    [InlineData("NULL, NULL, NULL, 'note', NULL, false")]
    [InlineData("NULL, NULL, NULL, NULL, NULL, true")]
    public async Task Check_in_fields_are_all_or_nothing(string checkIn)
    {
        var s = await SetupAsync();

        await ShouldFailAsync(() => InsertAsync(s, checkIn), PostgresErrorCodes.CheckViolation, "CK_EquipmentLoans_CheckIn");
    }

    [Fact]
    public async Task Return_condition_must_be_a_known_condition()
    {
        var s = await SetupAsync();

        await ShouldFailAsync(() => InsertAsync(s, "'2031-03-13 05:00Z', @user, 'Lost', NULL, NULL, false"),
            PostgresErrorCodes.CheckViolation, "CK_EquipmentLoans_ReturnCondition");
    }

    [Theory]
    [InlineData("NULL, " + Photo)]
    [InlineData("'Cracked', NULL")]
    public async Task Damaged_needs_a_note_and_a_photo(string noteAndPhoto)
    {
        var s = await SetupAsync();

        await ShouldFailAsync(() => InsertAsync(s, $"'2031-03-13 05:00Z', @user, 'Damaged', {noteAndPhoto}, false"),
            PostgresErrorCodes.CheckViolation, "CK_EquipmentLoans_Damaged");
    }

    [Fact]
    public async Task Check_in_cannot_be_before_checkout()
    {
        var s = await SetupAsync();

        await ShouldFailAsync(() => InsertAsync(s, "'2031-03-13 03:59Z', @user, 'Good', NULL, NULL, false"),
            PostgresErrorCodes.CheckViolation, "CK_EquipmentLoans_CheckedInAt");
    }

    [Fact]
    public async Task Due_must_be_after_checkout()
    {
        var s = await SetupAsync();

        await ShouldFailAsync(() => InsertAsync(s, due: Out), PostgresErrorCodes.CheckViolation, "CK_EquipmentLoans_DueAt");
    }

    [Theory]
    [InlineData("'../../etc/passwd'")]
    [InlineData("'0123456789abcdef0123456789abcdef.gif'")]
    [InlineData("'/a/0123456789abcdef0123456789abcd.jpg'")]
    public async Task Photo_path_is_a_random_file_name_only(string path)
    {
        var s = await SetupAsync();

        await ShouldFailAsync(() => InsertAsync(s, $"'2031-03-13 05:00Z', @user, 'Good', NULL, {path}, false"),
            PostgresErrorCodes.CheckViolation, "CK_EquipmentLoans_DamagePhotoPath");
    }
}
