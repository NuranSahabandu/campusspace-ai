using System.Text;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Options;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Extensions;

public static class AgentServiceExtensions
{
    /// <summary>
    /// The agent service connection (§10.12): validated options, the typed AgentClient that sends X-Service-Key, and
    /// the services that start runs and poll them.
    /// </summary>
    public static IServiceCollection AddAgentService(this IServiceCollection services)
    {
        services.AddOptions<AgentServiceOptions>()
            .BindConfiguration(AgentServiceOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => Encoding.UTF8.GetByteCount(o.ServiceKey ?? string.Empty) >= AgentServiceOptions.MinKeyBytes,
                AgentServiceOptions.KeyTooShortMessage)
            .Validate<IOptions<AgentToolsOptions>>((o, tools) => o.ServiceKey != tools.Value.Key,
                AgentServiceOptions.SameKeyMessage)
            .ValidateOnStart();

        services.AddHttpClient<IAgentClient, AgentClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AgentServiceOptions>>().Value;
                client.BaseAddress = BaseAddress(options.BaseUrl);
                client.Timeout = AgentClient.Timeout;
                client.DefaultRequestHeaders.Add(AgentClient.ServiceKeyHeader, options.ServiceKey);
            })
            // IHttpClientFactory's own logging must never print the key.
            .RedactLoggedHeaders(_ => true);

        services.AddScoped<IAgentRunStarter, AgentRunStarter>();
        return services;
    }

    /// <summary>With a trailing slash, so relative routes ("workflows") resolve under any base path.</summary>
    public static Uri BaseAddress(string baseUrl) => new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
}
