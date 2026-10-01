using CampusSpace.Api.Data;
using CampusSpace.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Extensions;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        // Read the connection string when the context is built, so test overrides are picked up.
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Default is not set. Run ./scripts/dev-secrets.sh (see README).");
            options.UseNpgsql(connectionString);
        });
        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<SeedOptions>, SeedOptionsValidator>();
        return services;
    }

    /// <summary>
    /// Runs at startup, before the host starts. Migrates only when Database:MigrateOnStartup is on (Development). When
    /// it is off but Seed:OnStartup is on (Production), a pending migration stops startup with the command to run,
    /// because the restricted app user can't apply it. Then seeds: the full Development seed, or only the reference seed
    /// in Production. Resolving the options here runs their startup checks (SSL, seed password) first.
    /// </summary>
    public static async Task PrepareDatabaseAsync(this WebApplication app)
    {
        var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var seed = app.Services.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CampusSpace.Api.Database");
        var reference = app.Environment.IsProduction();
        logger.LogInformation("Database: migrations on startup {Migrate}; seed {Seed}", database.MigrateOnStartup ? "on" : "off",
            !seed.OnStartup ? "off" : reference ? "reference data only" : "development data");
        if (!database.MigrateOnStartup && !seed.OnStartup)
            return;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (database.MigrateOnStartup)
            await db.Database.MigrateAsync();
        else if ((await db.Database.GetPendingMigrationsAsync()).Count() is var pending and > 0)
            throw new InvalidOperationException(PendingMigrationsMessage(pending));

        if (seed.OnStartup && reference)
            await Seed.SeedReferenceAsync(db, seed.DemoPassword!);
        else if (seed.OnStartup)
            await Seed.SeedAsync(db, seed.DemoPassword!);
    }

    public static string PendingMigrationsMessage(int pending) =>
        $"{pending} database migration(s) are pending and Database:MigrateOnStartup is off. Apply them with the database " +
        "owner role (README, Deploy: dotnet ef migrations script --idempotent), then start the API again.";
}
