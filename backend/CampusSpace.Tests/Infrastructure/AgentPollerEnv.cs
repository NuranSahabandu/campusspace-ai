using System.Net.Http.Json;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// A seeded API of its own for poller tests: the poller scans every run in its database, so it can't share one. The
/// clock starts at the real now (see MutableTimeProvider) and the agent service is the factory's FakeAgentClient.
/// </summary>
public sealed class AgentPollerEnv : IAsyncDisposable
{
    private AgentPollerEnv(CustomWebApplicationFactory factory, MutableTimeProvider clock) => (Factory, Clock) = (factory, clock);

    public CustomWebApplicationFactory Factory { get; }
    public MutableTimeProvider Clock { get; }
    public FakeAgentClient Agent => Factory.AgentClient;
    public AgentRunPoller Poller => Factory.Services.GetRequiredService<AgentRunPoller>();

    public static async Task<AgentPollerEnv> CreateAsync(PostgresFixture fixture)
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var factory = await fixture.CreateIsolatedFactoryAsync(clock);
        await using (var scope = factory.Services.CreateAsyncScope())
            await Seed.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "Test#Password1");
        return new AgentPollerEnv(factory, clock);
    }

    /// <summary>A 3-hour request (the demo's length) by a new student rep, or a new lecturer. Returns its id and run id.</summary>
    public async Task<(long RequestId, Guid RunId)> SubmitAsync(bool lecturer = false, int weekdaysAhead = 20)
    {
        var start = BookingRequestTestData.FutureStart(weekdaysAhead);
        HttpClient client;
        long? clubId = null;
        if (lecturer)
            (client, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.Lecturer);
        else
            (client, _, clubId) = await BookingRequestTestData.StudentRepAsync(Factory);
        var response = await client.PostAsJsonAsync(BookingRequestTestData.Url,
            BookingRequestTestData.Body(clubId, start: start, hours: 3, budget: lecturer ? 0m : 8000m));
        response.EnsureSuccessStatusCode();
        var id = (await response.ReadJsonAsync()).GetProperty("id").GetInt64();
        var run = await QueryAsync(db => db.AgentRuns.Where(r => r.RequestId == id).Select(r => r.Id).SingleAsync());
        return (id, run);
    }

    /// <summary>
    /// A request paused for approval, as 3.3 leaves it: submitted, then one tick over the awaiting fixture (the trace, the
    /// Draft quote, PendingApproval). The agent then reads as the matching completed fixture, as if finalize had passed.
    /// </summary>
    public async Task<(long RequestId, Guid RunId)> ToPendingApprovalAsync(bool lecturer = false, int weekdaysAhead = 20)
    {
        var (requestId, runId) = await SubmitAsync(lecturer, weekdaysAhead);
        AgentReturns(AgentFixtures.View(lecturer ? AgentFixtures.AwaitingLecturer : AgentFixtures.AwaitingStudent));
        await Poller.PollOnceAsync();
        AgentReturns(AgentFixtures.View(lecturer ? AgentFixtures.CompletedLecturer : AgentFixtures.CompletedStudent));
        return (requestId, runId);
    }

    /// <summary>A Facilities Officer (a real user: decisions store the officer's id), created once per environment.</summary>
    public async Task<(HttpClient Client, long UserId)> OfficerAsync() =>
        _officer ??= await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);

    private (HttpClient Client, long UserId)? _officer;

    /// <summary>POST /api/booking-requests/{id}/{action} as the officer (approve, reject, request-revision, cancel).</summary>
    public async Task<HttpResponseMessage> DecideAsync(long requestId, string action, object? body = null)
    {
        var (client, _) = await OfficerAsync();
        return await client.PostAsJsonAsync($"{BookingRequestTestData.Url}/{requestId}/{action}", body ?? new { });
    }

    /// <summary>Changes one PolicySettings value directly (tests about a policy changed after the proposal).</summary>
    public Task SetPolicyAsync(string key, string value) =>
        QueryAsync(db => db.PolicySettings.Where(p => p.Key == key).ExecuteUpdateAsync(s => s.SetProperty(p => p.Value, value)));

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task<AgentRun> RunAsync(Guid runId) => QueryAsync(db => db.AgentRuns.AsNoTracking().SingleAsync(r => r.Id == runId));

    public Task<BookingRequest> RequestAsync(long id) =>
        QueryAsync(db => db.BookingRequests.AsNoTracking().Include(r => r.StatusHistory).SingleAsync(r => r.Id == id));

    /// <summary>Every run reads as <paramref name="view"/> (a fixture), whatever its thread id.</summary>
    public void AgentReturns(AgentWorkflowView view) => Agent.Get = (_, _) => Task.FromResult(FakeAgentClient.Ok(view));

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}
