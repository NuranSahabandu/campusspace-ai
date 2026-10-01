using Microsoft.Extensions.Options;
using Npgsql;

namespace CampusSpace.Api.Options;

/// <summary>
/// Bound from the "Database" section. MigrateOnStartup: true in Development only; in Production the app user has no DDL
/// rights, so migrations are applied separately with the owner role (README, Deploy). RequireSsl: true in Production
/// (plan §15.3), so a connection string below SslMode=Require stops startup.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool MigrateOnStartup { get; set; }

    public bool RequireSsl { get; set; }
}

/// <summary>Checks ConnectionStrings:Default against <see cref="DatabaseOptions.RequireSsl"/>. Messages never contain the string.</summary>
public sealed class DatabaseOptionsValidator(IConfiguration configuration) : IValidateOptions<DatabaseOptions>
{
    public const string SslMessage =
        "Database:RequireSsl is on, so ConnectionStrings:Default must use SslMode=Require (or VerifyCA/VerifyFull).";

    public ValidateOptionsResult Validate(string? name, DatabaseOptions options) =>
        !options.RequireSsl || UsesSsl(configuration.GetConnectionString("Default"))
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(SslMessage);

    public static bool UsesSsl(string? connectionString)
    {
        try
        {
            return new NpgsqlConnectionStringBuilder(connectionString).SslMode >= SslMode.Require;
        }
        catch (ArgumentException)
        {
            return false; // a malformed string; its own error comes when the DbContext is built
        }
    }
}
