using CampusSpace.Api.Extensions;
using CampusSpace.Api.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CampusSpace.Tests.Unit;

public class AgentServiceOptionsValidationTests
{
    private const string ToolsKey = "tools-key-tools-key-tools-key-tools-01";

    private static AgentServiceOptions Resolve(string? serviceKey)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentTools:Key"] = ToolsKey,
            ["AgentService:ServiceKey"] = serviceKey,
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddAgentService()
            .AddOptions<AgentToolsOptions>().BindConfiguration(AgentToolsOptions.SectionName).Services
            .BuildServiceProvider();
        return services.GetRequiredService<IOptions<AgentServiceOptions>>().Value;
    }

    [Fact]
    public void Missing_key_fails() => ((Action)(() => Resolve(null))).Should().Throw<OptionsValidationException>();

    [Fact]
    public void Key_shorter_than_32_bytes_fails_with_a_clear_message() =>
        ((Action)(() => Resolve(new string('s', 31)))).Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(AgentServiceOptions.KeyTooShortMessage);

    [Fact]
    public void Key_equal_to_the_agent_tools_key_fails() =>
        ((Action)(() => Resolve(ToolsKey))).Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(AgentServiceOptions.SameKeyMessage);

    [Fact]
    public void A_valid_key_gets_the_documented_defaults()
    {
        var options = Resolve(new string('s', 32));

        options.BaseUrl.Should().Be("http://localhost:8000");
        (options.PollSeconds, options.RunTimeoutMinutes, options.StartTimeoutMinutes, options.InlineStartTimeoutSeconds)
            .Should().Be((3, 4, 2, 3));
        options.PollerEnabled.Should().BeTrue();
    }
}
