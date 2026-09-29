using System.Collections.Concurrent;
using CampusSpace.Api.Agents;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// The agent service for API tests (CustomWebApplicationFactory registers it as IAgentClient). By default a start is
/// accepted and every run reads as running. Tests that change a delegate must use their own factory
/// (fixture.CreateIsolatedFactoryAsync), because the shared factory's fake is shared too.
/// </summary>
public sealed class FakeAgentClient : IAgentClient
{
    public Func<Guid, long, CancellationToken, Task<AgentCallResult<AgentWorkflowAccepted>>> Start { get; set; } =
        (thread, _, _) => Task.FromResult(Accepted(thread));

    public Func<Guid, CancellationToken, Task<AgentCallResult<AgentWorkflowView>>> Get { get; set; } =
        (thread, _) => Task.FromResult(Ok(RunningView(thread)));

    public Func<Guid, string, string?, CancellationToken, Task<AgentCallResult<AgentWorkflowAccepted>>> Resume { get; set; } =
        (thread, _, _, _) => Task.FromResult(Accepted(thread));

    /// <summary>Every call in order: ("start" | "get" | "resume", thread, decision).</summary>
    public ConcurrentQueue<(string Call, Guid Thread, string? Decision)> Calls { get; } = new();

    public Task<AgentCallResult<AgentWorkflowAccepted>> StartAsync(Guid threadId, long requestId, CancellationToken ct = default)
    {
        Calls.Enqueue(("start", threadId, null));
        return Start(threadId, requestId, ct);
    }

    public Task<AgentCallResult<AgentWorkflowView>> GetAsync(Guid threadId, CancellationToken ct = default)
    {
        Calls.Enqueue(("get", threadId, null));
        return Get(threadId, ct);
    }

    public Task<AgentCallResult<AgentWorkflowAccepted>> ResumeAsync(Guid threadId, string decision, string? notes, CancellationToken ct = default)
    {
        Calls.Enqueue(("resume", threadId, decision));
        return Resume(threadId, decision, notes, ct);
    }

    public static AgentCallResult<AgentWorkflowAccepted> Accepted(Guid thread) =>
        new(AgentCallOutcome.Ok, new AgentWorkflowAccepted(thread.ToString(), AgentWorkflowStatuses.Running));

    public static AgentCallResult<AgentWorkflowView> Ok(AgentWorkflowView view) => new(AgentCallOutcome.Ok, view);

    public static AgentCallResult<T> Unavailable<T>(string detail = "network error") => AgentCallResult<T>.Unavailable(detail);

    public static AgentCallResult<T> NotFound<T>() => new(AgentCallOutcome.NotFound, default, "HTTP 404");

    /// <summary>A run that has done nothing yet (the agent service's view right after a start).</summary>
    public static AgentWorkflowView RunningView(Guid thread) => new(
        thread.ToString(), AgentWorkflowStatuses.Running, 1, null, null, null, null, [], [], [], null, null, "stub",
        DateTimeOffset.UtcNow, null, null);
}
