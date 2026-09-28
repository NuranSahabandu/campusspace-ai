using System.Collections.Concurrent;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Agents;

/// <summary>
/// Tracks agent runs in the background (§7.1 rule 6): every AgentService:PollSeconds it processes the Queued and
/// Running runs, oldest first, each in its own scope, so one bad run is logged and never stops the others. Off when
/// AgentService:PollerEnabled is false (Testing); tests call <see cref="PollOnceAsync"/> directly.
/// </summary>
public sealed class AgentRunPoller(
    IServiceScopeFactory scopes, IOptions<AgentServiceOptions> options, TimeProvider clock, ILogger<AgentRunPoller> logger)
    : BackgroundService
{
    public const int BatchSize = 50;

    /// <summary>The last outage detail per run (for example "HTTP 401"), so the watchdog can say why a run never started.</summary>
    private readonly ConcurrentDictionary<Guid, string> _lastFailure = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PollerEnabled)
        {
            logger.LogInformation("Agent run poller is disabled (AgentService:PollerEnabled = false)");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds), clock);
        try
        {
            do
            {
                try
                {
                    await PollOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Agent run poll failed; retrying on the next tick");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>One tick. Returns how many runs it looked at.</summary>
    public async Task<int> PollOnceAsync(CancellationToken ct = default)
    {
        List<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            ids = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentRuns.AsNoTracking()
                .Where(r => r.Status == AgentRunStatuses.Queued || r.Status == AgentRunStatuses.Running)
                .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
                .Select(r => r.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        foreach (var id in ids)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var detail = await scope.ServiceProvider.GetRequiredService<IAgentRunSync>()
                    .ProcessAsync(id, _lastFailure.GetValueOrDefault(id), ct);
                if (detail is null)
                    _lastFailure.TryRemove(id, out _);
                else
                    _lastFailure[id] = detail;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Agent run {RunId} could not be processed; continuing with the next run", id);
            }
        }

        // Forget runs that are no longer polled (they finished, or left the batch).
        foreach (var id in _lastFailure.Keys.Except(ids))
            _lastFailure.TryRemove(id, out _);
        return ids.Count;
    }
}
