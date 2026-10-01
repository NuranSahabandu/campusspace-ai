using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Photos;

/// <summary>
/// Bound from the "R2" section (env R2__AccountId, R2__AccessKeyId, R2__SecretAccessKey, R2__Bucket; user-secrets
/// locally via scripts/dev-secrets.sh). Never in appsettings or Git. Blank values mean "not set". All four set → photos
/// go to the R2 bucket; none → the local folder; anything in between is a startup error.
/// </summary>
public sealed class R2Options
{
    public const string SectionName = "R2";

    public string? AccountId { get; set; }
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? Bucket { get; set; }

    /// <summary>The environment-variable names of the settings that are not set.</summary>
    public IReadOnlyList<string> Missing =>
    [
        .. new (string Name, string? Value)[]
        {
            ("R2__AccountId", AccountId), ("R2__AccessKeyId", AccessKeyId),
            ("R2__SecretAccessKey", SecretAccessKey), ("R2__Bucket", Bucket),
        }.Where(s => string.IsNullOrWhiteSpace(s.Value)).Select(s => s.Name),
    ];

    public bool IsConfigured => Missing.Count == 0;

    public const string AllNames = "R2__AccountId, R2__AccessKeyId, R2__SecretAccessKey and R2__Bucket";
}

/// <summary>
/// Startup checks for <see cref="R2Options"/>. Messages name settings only, never a value. Production refuses to start
/// without R2: Render's disk is wiped on every deploy, so local photos would be lost.
/// </summary>
public sealed partial class R2OptionsValidator(IHostEnvironment environment) : IValidateOptions<R2Options>
{
    public const string ProductionMessage =
        "Production requires R2 (set " + R2Options.AllNames + "): damage photos must not go to the local disk.";

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex AccountIdPattern();

    // R2 bucket names: 3–63 lower-case letters, digits and hyphens, not starting or ending with a hyphen.
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")]
    private static partial Regex BucketPattern();

    public ValidateOptionsResult Validate(string? name, R2Options options)
    {
        var missing = options.Missing;
        if (missing.Count == 4)
            return environment.IsProduction() ? ValidateOptionsResult.Fail(ProductionMessage) : ValidateOptionsResult.Success;
        if (missing.Count > 0)
            return ValidateOptionsResult.Fail(
                $"R2 is only partly configured: set all of {R2Options.AllNames}, or none. Missing: {string.Join(", ", missing)}.");

        var errors = new List<string>();
        if (!AccountIdPattern().IsMatch(options.AccountId!.Trim()))
            errors.Add("R2__AccountId must be the 32-character Cloudflare account id.");
        if (!BucketPattern().IsMatch(options.Bucket!.Trim()))
            errors.Add("R2__Bucket is not a valid bucket name.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
