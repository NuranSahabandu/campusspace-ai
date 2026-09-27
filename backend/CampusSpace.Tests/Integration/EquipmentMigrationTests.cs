using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;

namespace CampusSpace.Tests.Integration;

/// <summary>The database itself enforces the equipment rules, even for rows written without the API.</summary>
[Collection(PostgresCollection.Name)]
public class EquipmentMigrationTests(PostgresFixture fixture)
{
    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static string Code() => EquipmentTestData.UniqueTypeCode();

    private string InsertType(string code, string category = "Audio", string fee = "10") =>
        $"""INSERT INTO "EquipmentTypes" ("Code", "Name", "Category", "FeePerBooking", "CreatedAt", "UpdatedAt") VALUES ('{code}', 'X', '{category}', {fee}, now(), now()) RETURNING "Id" """;

    private async Task<long> InsertTypeAsync() => (long)(await ScalarAsync(InsertType(Code())))!;

    private static string InsertItem(long typeId, string condition = "Good", string status = "Available") =>
        $"""INSERT INTO "EquipmentItems" ("TypeId", "AssetTag", "Condition", "Status", "CreatedAt", "UpdatedAt") VALUES ({typeId}, '{EquipmentTestData.UniqueAssetTag()}', '{condition}', '{status}', now(), now())""";

    private async Task ShouldViolateCheckAsync(string sql, string constraint)
    {
        var act = () => ScalarAsync(sql);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.Should().Match<PostgresException>(e => e.SqlState == PostgresErrorCodes.CheckViolation && e.ConstraintName == constraint);
    }

    [Fact]
    public async Task Type_checks_reject_a_bad_category_negative_fee_and_bad_code()
    {
        await ShouldViolateCheckAsync(InsertType(Code(), category: "Furniture"), "CK_EquipmentTypes_Category");
        await ShouldViolateCheckAsync(InsertType(Code(), fee: "-0.01"), "CK_EquipmentTypes_FeePerBooking");
        await ShouldViolateCheckAsync(InsertType("mic-wireless"), "CK_EquipmentTypes_Code_Format");
        await ShouldViolateCheckAsync(InsertType("M"), "CK_EquipmentTypes_Code_Format");
    }

    [Fact]
    public async Task Item_checks_reject_a_bad_status_or_condition()
    {
        var typeId = await InsertTypeAsync();

        await ShouldViolateCheckAsync(InsertItem(typeId, status: "Lost"), "CK_EquipmentItems_Status");
        await ShouldViolateCheckAsync(InsertItem(typeId, condition: "Broken"), "CK_EquipmentItems_Condition");
    }

    [Fact]
    public async Task A_type_cannot_substitute_itself()
    {
        var typeId = await InsertTypeAsync();

        await ShouldViolateCheckAsync(
            $"""INSERT INTO "EquipmentSubstitutes" ("TypeId", "SubstituteTypeId", "CreatedAt", "UpdatedAt") VALUES ({typeId}, {typeId}, now(), now())""",
            "CK_EquipmentSubstitutes_NotSelf");
    }

    [Fact]
    public async Task Features_code_is_unique_through_the_alternate_key_only()
    {
        var constraints = await ScalarAsync(
            """SELECT string_agg(conname, ',' ORDER BY conname) FROM pg_constraint WHERE conrelid = '"Features"'::regclass AND contype = 'u'""");
        var indexes = await ScalarAsync(
            """SELECT string_agg(indexname, ',' ORDER BY indexname) FROM pg_indexes WHERE tablename = 'Features'""");

        constraints.Should().Be("AK_Features_Code");
        indexes.Should().Be("AK_Features_Code,PK_Features");
    }

    [Fact]
    public async Task A_duplicate_feature_code_written_directly_fails_on_the_alternate_key()
    {
        var code = "f_" + Guid.NewGuid().ToString("N")[..12];
        var insert = $"""INSERT INTO "Features" ("Code", "Name", "CreatedAt", "UpdatedAt") VALUES ('{code}', 'X', now(), now())""";
        await ScalarAsync(insert);

        var act = () => ScalarAsync(insert);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.Should().Match<PostgresException>(e => e.SqlState == PostgresErrorCodes.UniqueViolation && e.ConstraintName == "AK_Features_Code");
    }

    [Fact]
    public async Task Equipment_foreign_key_columns_are_indexed()
    {
        var indexes = (string?)await ScalarAsync(
            """SELECT string_agg(indexdef, E'\n') FROM pg_indexes WHERE tablename IN ('EquipmentTypes', 'EquipmentItems', 'EquipmentSubstitutes')""");

        indexes.Should().Contain("(\"CoveredByFeatureCode\")")
            .And.Contain("(\"TypeId\", \"Status\")")
            .And.Contain("(\"SubstituteTypeId\")");
    }
}
