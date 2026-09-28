using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Options;

/// <summary>
/// Bound from the "AgentTools" section (env AgentTools__Key). Key is the X-Agent-Key the agent service's tools send to
/// /internal/agent-tools. It comes from user-secrets (dev) or environment variables, never appsettings, and differs
/// from AgentService:ServiceKey (X-Service-Key, .NET → agent service).
/// </summary>
public sealed class AgentToolsOptions
{
    public const string SectionName = "AgentTools";
    public const int MinKeyBytes = 32;
    public const string KeyTooShortMessage = "AgentTools:Key must be at least 32 bytes. Generate one with: openssl rand -hex 32";

    [Required] public string Key { get; set; } = string.Empty;
}
