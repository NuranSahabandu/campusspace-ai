using CampusSpace.Api.Data;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CampusSpace.Tests.Integration;

/// <summary>The database itself enforces the pricing and policy rules, even for rows written without the API.</summary>
[Collection(PostgresCollection.Name)]
public class PricingPolicyMigrationTests(PostgresFixture fixture)
{
    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string InsertRule(string roomType = "ComputerLab", string role = "Student", string rate = "100",
        string exempt = "false", string? validFrom = null) =>
        $"""INSERT INTO "PricingRules" ("RoomType", "RequesterRole", "HourlyRate", "IsExempt", "ValidFrom", "CreatedAt", "UpdatedAt") VALUES ('{roomType}', '{role}', {rate}, {exempt}, '{validFrom ?? PricingTestData.UniquePastDate().ToString("yyyy-MM-dd")}', now(), now())""";

    private async Task ShouldViolateAsync(string sql, string sqlState, string constraint)
    {
        var act = () => ExecuteAsync(sql);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.Should().Match<PostgresException>(e => e.SqlState == sqlState && e.ConstraintName == constraint);
    }

    [Fact]
    public async Task Pricing_checks_reject_bad_rows()
    {
        const string check = PostgresErrorCodes.CheckViolation;
        await ShouldViolateAsync(InsertRule(roomType: "Gym"), check, "CK_PricingRules_RoomType");
        await ShouldViolateAsync(InsertRule(role: "Admin"), check, "CK_PricingRules_RequesterRole");
        await ShouldViolateAsync(InsertRule(rate: "-0.01"), check, "CK_PricingRules_HourlyRate");
        await ShouldViolateAsync(InsertRule(rate: "100", exempt: "true"), check, "CK_PricingRules_Exempt_ZeroRate");
        await ExecuteAsync(InsertRule(rate: "0", exempt: "true"));
    }

    [Fact]
    public async Task Pricing_unique_index_rejects_a_second_rule_for_the_same_pair_and_date()
    {
        var date = PricingTestData.UniquePastDate().ToString("yyyy-MM-dd");
        await ExecuteAsync(InsertRule(role: "Lecturer", rate: "0", exempt: "true", validFrom: date));

        await ShouldViolateAsync(InsertRule(role: "Lecturer", rate: "0", exempt: "true", validFrom: date),
            PostgresErrorCodes.UniqueViolation, "IX_PricingRules_RoomType_RequesterRole_ValidFrom");
    }

    [Fact]
    public async Task Policy_checks_reject_unknown_keys_and_value_types()
    {
        await ShouldViolateAsync(
            """INSERT INTO "PolicySettings" ("Key", "Value", "ValueType", "Description", "UpdatedAt") VALUES ('max_group_size', '1', 'int', 'x', now())""",
            PostgresErrorCodes.CheckViolation, "CK_PolicySettings_Key");
        await ShouldViolateAsync(
            """UPDATE "PolicySettings" SET "ValueType" = 'string' WHERE "Key" = 'max_open_requests'""",
            PostgresErrorCodes.CheckViolation, "CK_PolicySettings_ValueType");
    }

    [Fact]
    public async Task A_fresh_migration_inserts_the_default_policy_exactly()
    {
        await using var db = PostgresFixture.CreateDbContext(await fixture.CreateDatabaseAsync());

        var rows = await db.PolicySettings.AsNoTracking().ToListAsync();

        rows.Select(r => (r.Key, r.ValueType, r.Value, r.Description))
            .Should().BeEquivalentTo(PolicySettingDefaults.All);
        rows.Should().OnlyContain(r => r.UpdatedById == null && r.UpdatedAt == new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
