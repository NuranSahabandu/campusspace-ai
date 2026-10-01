using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Options;

/// <summary>
/// Bound from the "AgentService" section. ServiceKey is the X-Service-Key .NET sends to the agent service's /workflows
/// routes (env AgentService__ServiceKey). It comes from user-secrets (dev) or environment variables, never appsettings,
/// and differs from AgentTools:Key (X-Agent-Key, agent tools → .NET). The timings drive the AgentRunPoller and its
/// watchdog (plan §10.10: a run Running for more than 4 minutes is failed).
/// </summary>
public sealed class AgentServiceOptions
{
    public const string SectionName = "AgentService";
    public const int MinKeyBytes = 32;
    public const string KeyTooShortMessage =
        "AgentService:ServiceKey must be at least 32 bytes. Generate one with: openssl rand -hex 32";
    public const string SameKeyMessage = "AgentService:ServiceKey and AgentTools:Key must be different secrets.";

    [Required, Url] public string BaseUrl { get; set; } = "http://localhost:8000";

    [Required] public string ServiceKey { get; set; } = string.Empty;

    /// <summary>Seconds between poller ticks.</summary>
    [Range(1, 3600)] public int PollSeconds { get; set; } = 3;

    /// <summary>
    /// With no live run the poller makes no database query except this sweep (and wakes at once when this process
    /// creates a run), so Neon can scale to zero. Aligned to the clock, like Email:IdleSweepMinutes.
    /// </summary>
    [Range(1, 1440)] public int IdleSweepMinutes { get; set; } = 30;

    /// <summary>The watchdog fails a run that has been Running longer than this.</summary>
    [Range(1, 1440)] public int RunTimeoutMinutes { get; set; } = 4;

    /// <summary>The watchdog fails a run that is still Queued (never started) after this.</summary>
    [Range(1, 1440)] public int StartTimeoutMinutes { get; set; } = 2;

    /// <summary>
    /// How long submit and retry-agent wait for the agent service to accept a start before answering 202 anyway. A
    /// slower start leaves the run Queued for the poller, so a hung agent service never slows down submit.
    /// </summary>
    [Range(1, 60)] public int InlineStartTimeoutSeconds { get; set; } = 3;

    /// <summary>
    /// How long approve waits for the agent's finalize before answering 202 ApprovalInProgress (the poller then finishes the
    /// approval). 0 checks once.
    /// </summary>
    [Range(0, 60)] public int ApprovalWaitSeconds { get; set; } = 10;

    /// <summary>How often approve reads the run while it waits.</summary>
    [Range(50, 5000)] public int ApprovalPollMilliseconds { get; set; } = 500;

    /// <summary>Off in Testing, where tests call AgentRunPoller.PollOnceAsync themselves.</summary>
    public bool PollerEnabled { get; set; } = true;
}
