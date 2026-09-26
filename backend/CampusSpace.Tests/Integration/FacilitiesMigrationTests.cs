using CampusSpace.Api.Data.Configurations;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Npgsql;

namespace CampusSpace.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class FacilitiesMigrationTests(PostgresFixture fixture)
{
    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    [Fact]
    public async Task Btree_gist_extension_is_installed()
    {
        var count = await ScalarAsync("SELECT count(*) FROM pg_extension WHERE extname = 'btree_gist'");

        count.Should().Be(1L);
    }

    [Fact]
    public async Task Room_blackouts_have_a_gist_index_on_room_and_time_range()
    {
        var definition = (string?)await ScalarAsync(
            $"SELECT indexdef FROM pg_indexes WHERE tablename = 'RoomBlackouts' AND indexname = '{RoomBlackoutConfiguration.RoomTimeRangeIndex}'");

        definition.Should().NotBeNull();
        definition.Should().Contain("USING gist").And.Contain("\"RoomId\", \"TimeRange\"");
    }
}
