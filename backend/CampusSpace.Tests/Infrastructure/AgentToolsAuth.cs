using CampusSpace.Api.Auth;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>Clients for /internal/agent-tools, which accept only X-Agent-Key.</summary>
public static class AgentToolsAuth
{
    public const string Url = AgentKeyDefaults.RoutePrefix;

    /// <summary>A client that sends the factory's real agent key.</summary>
    public static HttpClient CreateClient(CustomWebApplicationFactory factory) =>
        WithAgentKey(factory.CreateClient(), factory.AgentToolsKey);

    public static HttpClient WithAgentKey(HttpClient client, string key)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation(AgentKeyDefaults.HeaderName, key);
        return client;
    }
}
