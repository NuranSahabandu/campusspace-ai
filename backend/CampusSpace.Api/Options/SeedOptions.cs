using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Options;

/// <summary>
/// Bound from the "Seed" section. OnStartup: Development (full seed) and Production (reference seed) both seed on
/// startup; tests don't. DemoPassword: appsettings.Development.json locally; in Production only the environment variable
/// Seed__DemoPassword, never Git.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool OnStartup { get; set; }

    public string? DemoPassword { get; set; }
}

/// <summary>
/// Startup checks for <see cref="SeedOptions"/>: seeding needs a password; Production refuses a short one and the public
/// Development one (it is in Git and the README). Messages name the setting, never a value.
/// </summary>
public sealed class SeedOptionsValidator(IHostEnvironment environment) : IValidateOptions<SeedOptions>
{
    public const int MinProductionPasswordLength = 12;

    public const string MissingMessage = "Seed:DemoPassword is not set (Production: the environment variable Seed__DemoPassword).";
    public const string WeakMessage = "Seed__DemoPassword must be at least 12 characters in Production.";
    public const string PublicMessage = "Seed__DemoPassword must not be the public Development password in Production.";

    /// <summary>The Development password as committed in appsettings.Development.json (public).</summary>
    public const string PublicDevelopmentPassword = "CampusSpace#2026";

    public ValidateOptionsResult Validate(string? name, SeedOptions options)
    {
        if (!options.OnStartup)
            return ValidateOptionsResult.Success;
        if (string.IsNullOrWhiteSpace(options.DemoPassword))
            return ValidateOptionsResult.Fail(MissingMessage);
        if (!environment.IsProduction())
            return ValidateOptionsResult.Success;
        if (options.DemoPassword.Length < MinProductionPasswordLength)
            return ValidateOptionsResult.Fail(WeakMessage);
        return options.DemoPassword == PublicDevelopmentPassword ? ValidateOptionsResult.Fail(PublicMessage) : ValidateOptionsResult.Success;
    }
}
