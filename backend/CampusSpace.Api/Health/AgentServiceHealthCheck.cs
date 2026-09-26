using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CampusSpace.Api.Health;

/// <summary>
/// Calls the agent service's GET /health (plan §10.12). Registered with failureStatus Degraded,
/// so /health stays 200 when only the agent service is down. Descriptions never carry exception detail.
/// </summary>
public sealed class AgentServiceHealthCheck(IHttpClientFactory httpClientFactory, ILogger<AgentServiceHealthCheck> logger)
    : IHealthCheck
{
    public const string ClientName = "agent-service";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.GetAsync("/health", cancellationToken);
            return response.StatusCode == HttpStatusCode.OK
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus,
                    $"Agent service returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            // Covers connection refused and the 3-second HttpClient timeout. Only the type is logged.
            logger.LogWarning("Agent service health check failed: {ExceptionType}", ex.GetType().Name);
            return new HealthCheckResult(context.Registration.FailureStatus, "Agent service unreachable");
        }
    }
}
