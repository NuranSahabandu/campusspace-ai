using System.Text.Json;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Options;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>
/// The Production settings file and the hosting helpers: PORT, CORS origin rules, the SSL check, and that no secret is in
/// any committed appsettings file.
/// </summary>
public class ProductionSettingsTests
{
    private static readonly string ApiFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CampusSpace.Api"));

    private static JsonElement Settings(string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(ApiFolder, file))).RootElement;

    [Fact]
    public void Production_turns_migrations_off_and_TLS_Swagger_and_forwarded_headers_on()
    {
        var production = Settings("appsettings.Production.json");

        production.GetProperty("Database").GetProperty("MigrateOnStartup").GetBoolean().Should().BeFalse();
        production.GetProperty("Database").GetProperty("RequireSsl").GetBoolean().Should().BeTrue();
        production.GetProperty("Seed").GetProperty("OnStartup").GetBoolean().Should().BeTrue();
        production.GetProperty("Swagger").GetProperty("Enabled").GetBoolean().Should().BeTrue();
        production.GetProperty("ForwardedHeaders").GetProperty("Enabled").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Production.json")]
    public void No_secret_is_committed(string file)
    {
        var text = File.ReadAllText(Path.Combine(ApiFolder, file));

        foreach (var secret in new[] { "ConnectionStrings", "\"Key\"", "ServiceKey", "BrevoApiKey", "SecretAccessKey", "AccessKeyId", "DemoPassword" })
            text.Should().NotContain(secret);
    }

    [Fact]
    public void The_public_Development_password_constant_matches_the_committed_one()
    {
        Settings("appsettings.Development.json").GetProperty("Seed").GetProperty("DemoPassword").GetString()
            .Should().Be(SeedOptionsValidator.PublicDevelopmentPassword);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("10000", "http://0.0.0.0:10000")]
    [InlineData("1", "http://0.0.0.0:1")]
    public void PORT_sets_the_listening_url(string? port, string? url)
    {
        HostingExtensions.UrlForPort(port).Should().Be(url);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("65536")]
    public void An_invalid_PORT_stops_startup(string port)
    {
        FluentActions.Invoking(() => HostingExtensions.UrlForPort(port)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CORS_accepts_the_deployed_https_origin_and_localhost()
    {
        HostingExtensions.CorsProblems(["https://campusspace.vercel.app", "http://localhost:5173", "http://127.0.0.1:5173"])
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.vercel.app")]
    [InlineData("http://campusspace.vercel.app")]
    [InlineData("https://campusspace.vercel.app/login")]
    [InlineData("campusspace.vercel.app")]
    public void CORS_refuses_wildcards_plain_http_paths_and_non_urls(string origin)
    {
        HostingExtensions.CorsProblems([origin]).Should().ContainSingle();
    }

    [Fact]
    public void CORS_refuses_an_empty_list()
    {
        HostingExtensions.CorsProblems([]).Should().ContainSingle().Which.Should().StartWith("Cors:AllowedOrigins is empty");
    }

    [Theory]
    [InlineData("Host=x;Database=d;Username=u;Password=p", false)]
    [InlineData("Host=x;Database=d;Username=u;Password=p;SslMode=Prefer", false)]
    [InlineData("Host=x;Database=d;Username=u;Password=p;SslMode=Require", true)]
    [InlineData("Host=x;Database=d;Username=u;Password=p;SSL Mode=VerifyFull", true)]
    [InlineData("not a connection string", false)]
    [InlineData(null, false)]
    public void The_SSL_check_needs_at_least_SslMode_Require(string? connectionString, bool ok)
    {
        DatabaseOptionsValidator.UsesSsl(connectionString).Should().Be(ok);
    }
}
