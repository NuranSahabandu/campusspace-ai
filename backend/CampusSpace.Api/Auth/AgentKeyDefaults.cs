namespace CampusSpace.Api.Auth;

/// <summary>
/// The service-to-service scheme for the agent tools (§7.1 rule 3, §15.3). Only the AgentTools policy uses it, so the
/// agent key never authenticates an /api route and a user JWT never authenticates an /internal route.
/// </summary>
public static class AgentKeyDefaults
{
    public const string Scheme = "AgentKey";
    public const string HeaderName = "X-Agent-Key";
    public const string Policy = "AgentTools";

    /// <summary>The single claim of the agent principal. It has no sub and no role.</summary>
    public const string ClaimType = "agent";
    public const string ClaimValue = "agent-service";

    public const string RoutePrefix = "/internal/agent-tools";
}
