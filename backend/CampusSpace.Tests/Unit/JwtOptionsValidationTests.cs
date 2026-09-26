using CampusSpace.Api.Extensions;
using CampusSpace.Api.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CampusSpace.Tests.Unit;

public class JwtOptionsValidationTests
{
    private static JwtOptions Resolve(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = key,
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience",
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddJwtAuth()
            .BuildServiceProvider();
        return services.GetRequiredService<IOptions<JwtOptions>>().Value;
    }

    [Fact]
    public void Key_shorter_than_32_bytes_fails_with_a_clear_message()
    {
        var act = () => Resolve(new string('k', 31));

        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(JwtOptions.KeyTooShortMessage);
    }

    [Fact]
    public void Key_of_32_bytes_is_accepted_and_lifetime_defaults_to_120_minutes()
    {
        var options = Resolve(new string('k', 32));

        options.AccessTokenMinutes.Should().Be(120);
    }
}
