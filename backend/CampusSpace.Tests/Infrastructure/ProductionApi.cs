using CampusSpace.Api.Photos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// The real API in the Production environment (appsettings.Production.json) on a test database. Production needs R2,
/// TLS to the database and CORS origins, so this sets fake R2 values (the photo store is a <see cref="FakePhotoStore"/>,
/// nothing calls R2), turns RequireSsl off (Testcontainers has no TLS; DatabaseOptionsValidator is tested on its own),
/// sets the allowed origins and turns the startup seed off. <paramref name="settings"/> override any of these (a null
/// value removes the default).
/// </summary>
public sealed class ProductionApi : IAsyncDisposable
{
    public const string VercelOrigin = "https://campusspace-test.vercel.app";
    public const string LocalOrigin = "http://localhost:5173";

    private readonly CustomWebApplicationFactory _base;

    public ProductionApi(string connectionString, IDictionary<string, string?>? settings = null)
    {
        _base = new CustomWebApplicationFactory(connectionString, environment: "Production");
        var all = new Dictionary<string, string?>
        {
            ["R2:AccountId"] = "0123456789abcdef0123456789abcdef",
            ["R2:AccessKeyId"] = "test-access-key",
            ["R2:SecretAccessKey"] = "test-secret",
            ["R2:Bucket"] = "campusspace-test",
            ["Database:RequireSsl"] = "false",
            ["Cors:AllowedOrigins:0"] = VercelOrigin,
            ["Cors:AllowedOrigins:1"] = LocalOrigin,
            ["Seed:OnStartup"] = "false",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            all[key] = value;
        Factory = _base.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in all)
                if (value is not null)
                    builder.UseSetting(key, value);
            builder.ConfigureTestServices(services => services.AddSingleton<IPhotoStore>(Photos));
        });
    }

    public FakePhotoStore Photos { get; } = new();

    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>A client that does not follow redirects, so a test sees the 307.</summary>
    public HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A client whose requests carry X-Forwarded-Proto: https, as Render's proxy sends them.</summary>
    public HttpClient HttpsClient()
    {
        var client = Client();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _base.DisposeAsync();
    }
}
