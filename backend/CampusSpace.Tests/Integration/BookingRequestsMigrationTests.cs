using CampusSpace.Api.Models;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;

namespace CampusSpace.Tests.Integration;

/// <summary>The database itself enforces the booking request rules, even for rows written without the API.</summary>
[Collection(PostgresCollection.Name)]
public class BookingRequestsMigrationTests(PostgresFixture fixture)
{
    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static string InsertRequest(long requesterId, string attendees = "10", string status = "Submitted",
        string budget = "0", string end = "'2030-01-01 12:00Z'", string purpose = "Meeting") =>
        $"""INSERT INTO "BookingRequests" ("RequesterId", "Purpose", "Attendees", "RequestedStart", "RequestedEnd", "BudgetLkr", "Status", "CreatedAt", "UpdatedAt") VALUES ({requesterId}, '{purpose}', {attendees}, '2030-01-01 10:00Z', {end}, {budget}, '{status}', now(), now()) RETURNING "Id" """;

    private async Task ShouldViolateCheckAsync(string sql, string constraint)
    {
        var act = () => ScalarAsync(sql);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.Should().Match<PostgresException>(e => e.SqlState == PostgresErrorCodes.CheckViolation && e.ConstraintName == constraint);
    }

    [Fact]
    public async Task Request_checks_reject_bad_status_attendees_times_budget_and_purpose()
    {
        var (_, userId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Student);

        await ShouldViolateCheckAsync(InsertRequest(userId, status: "Draft"), "CK_BookingRequests_Status");
        await ShouldViolateCheckAsync(InsertRequest(userId, attendees: "0"), "CK_BookingRequests_Attendees");
        await ShouldViolateCheckAsync(InsertRequest(userId, attendees: "2001"), "CK_BookingRequests_Attendees");
        await ShouldViolateCheckAsync(InsertRequest(userId, end: "'2030-01-01 10:00Z'"), "CK_BookingRequests_RequestedEnd_After_Start");
        await ShouldViolateCheckAsync(InsertRequest(userId, budget: "-0.01"), "CK_BookingRequests_BudgetLkr");
        await ShouldViolateCheckAsync(InsertRequest(userId, purpose: "  "), "CK_BookingRequests_Purpose_NotBlank");

        var id = (long)(await ScalarAsync(InsertRequest(userId, attendees: "2000")))!;
        (await ScalarAsync($"""SELECT cardinality("RequiredFeatures") FROM "BookingRequests" WHERE "Id" = {id}""")).Should().Be(0);
    }

    [Fact]
    public async Task Line_quantity_and_history_status_checks_reject_bad_rows()
    {
        var (_, userId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Student);
        var type = await EquipmentTestData.CreateTypeAsync(fixture.Factory);
        var id = (long)(await ScalarAsync(InsertRequest(userId)))!;

        string Line(int quantity) =>
            $"""INSERT INTO "RequestedEquipmentLines" ("RequestId", "TypeId", "Quantity") VALUES ({id}, {type.Id}, {quantity})""";
        await ShouldViolateCheckAsync(Line(0), "CK_RequestedEquipmentLines_Quantity");
        await ShouldViolateCheckAsync(Line(51), "CK_RequestedEquipmentLines_Quantity");
        await ScalarAsync(Line(50));

        string History(string from, string to) =>
            $"""INSERT INTO "RequestStatusHistory" ("RequestId", "FromStatus", "ToStatus", "ChangedAt") VALUES ({id}, {from}, '{to}', now())""";
        await ShouldViolateCheckAsync(History("NULL", "Draft"), "CK_RequestStatusHistory_ToStatus");
        await ShouldViolateCheckAsync(History("'Draft'", "Submitted"), "CK_RequestStatusHistory_FromStatus");
        await ScalarAsync(History("NULL", "Submitted"));
    }

    [Fact]
    public async Task A_request_with_history_cannot_be_deleted()
    {
        var (_, userId) = await TestAuth.CreateUserClientAsync(fixture.Factory, Roles.Student);
        var id = (long)(await ScalarAsync(InsertRequest(userId)))!;
        await ScalarAsync($"""INSERT INTO "RequestStatusHistory" ("RequestId", "ToStatus", "ChangedAt") VALUES ({id}, 'Submitted', now())""");

        var act = () => ScalarAsync($"""DELETE FROM "BookingRequests" WHERE "Id" = {id}""");

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }
}
