using System.Net;
using System.Net.Http.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.BookingRequestTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>POST /api/booking-requests/{id}/retry-agent (plan §9: "New run after AgentFailed"), Facilities Officer only.</summary>
[Collection(PostgresCollection.Name)]
public class BookingRequestsRetryAgentTests(PostgresFixture fixture)
{
    private CustomWebApplicationFactory Factory => fixture.Factory;

    private static string RetryUrl(long id) => $"{Url}/{id}/retry-agent";

    private async Task<HttpClient> OfficerAsync() => (await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer)).Client;

    private static async Task<long> SubmitAsync(HttpClient client, long? clubId, int weekdaysAhead = 30)
    {
        var response = await client.PostAsJsonAsync(Url, Body(clubId, start: FutureStart(weekdaysAhead)));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt64();
    }

    private async Task<List<AgentRun>> RunsAsync(long requestId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentRuns.AsNoTracking()
            .Where(r => r.RequestId == requestId).OrderBy(r => r.RevisionNo).ToListAsync();
    }

    [Fact]
    public async Task An_officer_restarts_a_failed_request_with_revision_2_and_the_old_run_stays_failed()
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var id = await SubmitAsync(client, clubId);
        var failedRun = await AgentRunTestData.ToAgentFailedAsync(Factory, id);
        var (officer, officerId) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);

        var response = await officer.PostAsync(RetryUrl(id), null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location!.AbsolutePath.Should().Be($"{Url}/{id}");
        var body = await response.ReadJsonAsync();
        body.GetProperty("status").GetString().Should().Be(RequestStatuses.AgentProcessing);
        var last = body.GetProperty("history").EnumerateArray().Last();
        last.GetProperty("fromStatus").GetString().Should().Be(RequestStatuses.AgentFailed);
        last.GetProperty("toStatus").GetString().Should().Be(RequestStatuses.AgentProcessing);
        last.GetProperty("changedById").GetInt64().Should().Be(officerId);

        var runs = await RunsAsync(id);
        runs.Select(r => (r.RevisionNo, r.Status)).Should().Equal((1, AgentRunStatuses.Failed), (2, AgentRunStatuses.Running));
        runs[0].Id.Should().Be(failedRun);
        Factory.AgentClient.Calls.Should().Contain(("start", runs[1].Id, null));
    }

    [Fact]
    public async Task A_submitted_request_without_a_run_is_started_with_revision_1()
    {
        var (_, userId) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);
        var id = await InsertSubmittedAsync(Factory, userId);

        var response = await (await OfficerAsync()).PostAsync(RetryUrl(id), null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.ReadJsonAsync()).GetProperty("status").GetString().Should().Be(RequestStatuses.AgentProcessing);
        (await RunsAsync(id)).Select(r => (r.RevisionNo, r.Status)).Should().Equal((1, AgentRunStatuses.Running));
    }

    [Fact]
    public async Task A_submitted_request_that_already_has_a_live_run_is_a_409()
    {
        var (_, userId) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);
        var id = await InsertSubmittedAsync(Factory, userId);
        await AgentRunTestData.InsertRunAsync(Factory, id, AgentRunStatuses.Queued);

        var response = await (await OfficerAsync()).PostAsync(RetryUrl(id), null);

        (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString()
            .Should().Be(BookingRequestService.NotRestartableMessage);
        (await RunsAsync(id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Other_statuses_are_a_409()
    {
        var officer = await OfficerAsync();
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var processing = await SubmitAsync(client, clubId, 30);
        var pending = await SubmitAsync(client, clubId, 31);
        await AgentRunTestData.ToPendingApprovalAsync(Factory, pending);

        foreach (var id in new[] { processing, pending })
            (await (await officer.PostAsync(RetryUrl(id), null)).ShouldBeProblemAsync(409)).GetProperty("title").GetString()
                .Should().Be(BookingRequestService.NotRestartableMessage);
    }

    [Fact]
    public async Task Requesters_are_forbidden_and_an_unknown_request_is_404()
    {
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var id = await SubmitAsync(client, clubId);
        await AgentRunTestData.ToAgentFailedAsync(Factory, id);

        await (await client.PostAsync(RetryUrl(id), null)).ShouldBeProblemAsync(403);
        await (await TestAuth.CreateClient(Factory, Roles.Lecturer).PostAsync(RetryUrl(id), null)).ShouldBeProblemAsync(403);
        await (await Factory.CreateClient().PostAsync(RetryUrl(id), null)).ShouldBeProblemAsync(401);
        await (await (await OfficerAsync()).PostAsync(RetryUrl(long.MaxValue / 2), null)).ShouldBeProblemAsync(404);
        (await RunsAsync(id)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_retry_that_would_exceed_the_requesters_open_request_cap_is_a_409()
    {
        // The shared database keeps the default max_open_requests (3).
        var (client, _, clubId) = await StudentRepAsync(Factory);
        var failed = await SubmitAsync(client, clubId, 30);
        await SubmitAsync(client, clubId, 31);
        await SubmitAsync(client, clubId, 32);
        await AgentRunTestData.ToAgentFailedAsync(Factory, failed);
        await SubmitAsync(client, clubId, 33); // AgentFailed is not open, so this one fits: 3 open again

        var response = await (await OfficerAsync()).PostAsync(RetryUrl(failed), null);

        (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString()
            .Should().Be("The requester already has 3 open requests (the limit is 3)");
        (await RunsAsync(failed)).Should().ContainSingle(r => r.Status == AgentRunStatuses.Failed);
    }
}
