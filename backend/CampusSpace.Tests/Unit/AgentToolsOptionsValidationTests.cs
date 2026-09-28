using CampusSpace.Api.Auth;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CampusSpace.Tests.Unit;

public class AgentToolsOptionsValidationTests
{
    private static AgentToolsOptions Resolve(string? key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = new string('j', 32),
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience",
            ["AgentTools:Key"] = key,
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddJwtAuth()
            .BuildServiceProvider();
        return services.GetRequiredService<IOptions<AgentToolsOptions>>().Value;
    }

    [Fact]
    public void Missing_key_fails()
    {
        var act = () => Resolve(null);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Key_shorter_than_32_bytes_fails_with_a_clear_message()
    {
        var act = () => Resolve(new string('a', 31));

        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(AgentToolsOptions.KeyTooShortMessage);
    }

    [Fact]
    public void Key_of_32_bytes_is_accepted() => Resolve(new string('a', 32)).Key.Should().HaveLength(32);

    [Theory]
    [InlineData("the-right-key-the-right-key-1234", true)]
    [InlineData("the-right-key-the-right-key-1235", false)]
    [InlineData("the-right-key", false)]
    [InlineData("the-right-key-the-right-key-1234-and-more", false)]
    public void Keys_match_only_when_equal(string presented, bool expected) =>
        AgentKeyAuthenticationHandler.KeysMatch(presented, "the-right-key-the-right-key-1234").Should().Be(expected);
}
