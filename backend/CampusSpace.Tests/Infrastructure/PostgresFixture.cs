using CampusSpace.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// One postgres:16 container and one API factory, shared by every test in the "Postgres" collection.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public CustomWebApplicationFactory Factory { get; private set; } = null!;

    /// <summary>Connection string of the container's default database (the one Factory uses).</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Factory = new CustomWebApplicationFactory(_container.GetConnectionString());
        await Factory.MigrateAsync();
    }

    /// <summary>
    /// A new, migrated database on the shared container, for tests that need a table to themselves (seeding,
    /// pricing statuses, policy changes). Returns its connection string.
    /// </summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ToString();
        await using var db = CreateDbContext(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    /// <summary>The real API on its own new database, optionally with a frozen clock. Dispose it at the end of the test.</summary>
    public async Task<CustomWebApplicationFactory> CreateIsolatedFactoryAsync(TimeProvider? clock = null) =>
        new(await CreateDatabaseAsync(), clock);

    public static AppDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
