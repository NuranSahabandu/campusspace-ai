using CampusSpace.Api.Data;
using Microsoft.EntityFrameworkCore;

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
        return services;
    }

    /// <summary>Development only: apply pending migrations, then seed empty tables.</summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var demoPassword = app.Configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(demoPassword))
            throw new InvalidOperationException("Seed:DemoPassword is not set (see appsettings.Development.json).");

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await Seed.SeedAsync(db, demoPassword);
    }
}
