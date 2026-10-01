using CampusSpace.Api.Photos;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampusSpace.Tests.Unit;

/// <summary>
/// AddPhotoStorage: R2 when all four R2 settings are set, the local folder when none are, a startup error in between,
/// and Production refuses the local folder. Builds the real registration (no network: the R2 client is never called).
/// </summary>
public sealed class PhotoStoreSelectionTests
{
    private const string Secret = "SENTINEL-SECRET-77aa";

    private static readonly Dictionary<string, string?> AllR2 = new()
    {
        ["R2:AccountId"] = "0123456789abcdef0123456789abcdef",
        ["R2:AccessKeyId"] = "SENTINELACCESSKEY",
        ["R2:SecretAccessKey"] = Secret,
        ["R2:Bucket"] = "campusspace-photos-test",
    };

    private readonly CapturingLoggerProvider _logs = new();

    private ServiceProvider Build(string environment, Dictionary<string, string?> r2)
    {
        var settings = new Dictionary<string, string?>(r2) { ["Storage:DamagePhotosPath"] = Path.Combine(Path.GetTempPath(), "cs-unused") };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment, ContentRootPath = Path.GetTempPath() })
            .AddLogging(b => b.AddProvider(_logs))
            .AddPhotoStorage()
            .BuildServiceProvider();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void All_four_settings_select_R2(string environment)
    {
        using var services = Build(environment, AllR2);

        services.GetRequiredService<IPhotoStore>().Should().BeOfType<R2PhotoStore>().Which.Name.Should().Be("R2");
    }

    [Fact]
    public void No_settings_select_the_local_folder_outside_Production()
    {
        using var services = Build("Development", []);

        services.GetRequiredService<IPhotoStore>().Should().BeOfType<LocalPhotoStore>();
    }

    [Fact]
    public void Blank_values_count_as_not_set()
    {
        using var services = Build("Development", AllR2.ToDictionary(p => p.Key, _ => (string?)"  "));

        services.GetRequiredService<IPhotoStore>().Should().BeOfType<LocalPhotoStore>();
    }

    [Fact]
    public void A_partial_configuration_is_a_startup_error_naming_the_missing_keys_only()
    {
        using var services = Build("Development", AllR2.Where(p => p.Key != "R2:Bucket").ToDictionary());

        var error = FluentActions.Invoking(() => services.GetRequiredService<IOptions<R2Options>>().Value)
            .Should().Throw<OptionsValidationException>().Which;

        error.Message.Should().Contain("Missing: R2__Bucket").And.NotContain(Secret).And.NotContain("SENTINELACCESSKEY");
    }

    [Fact]
    public void Production_without_R2_refuses_to_start()
    {
        using var services = Build("Production", []);

        FluentActions.Invoking(() => services.GetRequiredService<IOptions<R2Options>>().Value)
            .Should().Throw<OptionsValidationException>().WithMessage(R2OptionsValidator.ProductionMessage);
    }

    [Theory]
    [InlineData("R2:AccountId", "not-an-account/evil.example?")]
    [InlineData("R2:Bucket", "Bad_Bucket")]
    public void Malformed_account_or_bucket_is_refused(string key, string value)
    {
        using var services = Build("Development", new Dictionary<string, string?>(AllR2) { [key] = value });

        FluentActions.Invoking(() => services.GetRequiredService<IOptions<R2Options>>().Value)
            .Should().Throw<OptionsValidationException>().Which.Message.Should().NotContain(value).And.NotContain(Secret);
    }

    [Theory]
    [InlineData(true, "Photo store: R2")]
    [InlineData(false, "Photo store: local folder")]
    public async Task The_startup_log_names_the_store_only(bool r2, string expected)
    {
        using var services = Build("Development", r2 ? AllR2 : []);
        var startup = services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<PhotoStorageStartup>().Single();

        await startup.StartAsync(CancellationToken.None);
        await (startup.ExecuteTask ?? Task.CompletedTask);

        var messages = _logs.Entries.Select(e => e.Message).ToList();
        messages.Should().Contain(expected);
        // With R2, the import runs; here it has no database, so it fails without stopping startup or leaking anything.
        string.Join("\n", messages).Should().NotContain(Secret).And.NotContain("SENTINELACCESSKEY")
            .And.NotContain("campusspace-photos-test").And.NotContain("0123456789abcdef0123456789abcdef");
    }
}
