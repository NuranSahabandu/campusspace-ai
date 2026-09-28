using System.Net;
using System.Text;
using System.Text.Json;
using CampusSpace.Api.Agents;
using CampusSpace.Api.Extensions;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CampusSpace.Tests.Unit;

/// <summary>The real AgentClient registration (base URL, X-Service-Key, redaction) over a recording fake handler.</summary>
public class AgentClientTests
{
    private const string ServiceKey = "service-key-service-key-service-key-0001";
    private static readonly Guid Thread = Guid.Parse("2b7c1f7e-4f3a-4b43-9d59-3f0f6a2f9c11");

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Key, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues(AgentClient.ServiceKeyHeader, out var keys) ? keys.Single() : null, body));
            return await respond(request, ct);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Accepted() =>
        Json(HttpStatusCode.Accepted, $$"""{"thread_id":"{{Thread}}","status":"running"}""");

    private static (IAgentClient Client, RecordingHandler Handler, CapturingLoggerProvider Logs) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
    {
        var handler = new RecordingHandler(respond);
        var logs = new CapturingLoggerProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentService:BaseUrl"] = "http://agent.test:8000",
            ["AgentService:ServiceKey"] = ServiceKey,
            ["AgentTools:Key"] = "tools-key-tools-key-tools-key-tools-01",
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
            .AddAgentService();
        var builder = services.AddHttpClient<IAgentClient, AgentClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        if (timeout is { } t)
            builder.ConfigureHttpClient(c => c.Timeout = t);
        return (services.BuildServiceProvider().GetRequiredService<IAgentClient>(), handler, logs);
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Always(Func<HttpResponseMessage> response) =>
        (_, _) => Task.FromResult(response());

    [Fact]
    public async Task Start_posts_snake_case_ids_with_the_service_key_and_202_is_ok()
    {
        var (client, handler, _) = Create(Always(Accepted));

        var result = await client.StartAsync(Thread, 42);

        result.Outcome.Should().Be(AgentCallOutcome.Ok);
        result.Value!.ThreadId.Should().Be(Thread.ToString());
        var sent = handler.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Path.Should().Be("/workflows");
        sent.Key.Should().Be(ServiceKey);
        using var body = JsonDocument.Parse(sent.Body!);
        body.RootElement.GetProperty("thread_id").GetString().Should().Be(Thread.ToString());
        body.RootElement.GetProperty("request_id").GetInt64().Should().Be(42);
        body.RootElement.EnumerateObject().Should().HaveCount(2);
    }

    [Fact]
    public async Task Start_409_is_already_exists_and_a_500_is_unavailable_without_a_retry()
    {
        var (conflictClient, conflictHandler, _) = Create(Always(() => new HttpResponseMessage(HttpStatusCode.Conflict)));
        (await conflictClient.StartAsync(Thread, 42)).Outcome.Should().Be(AgentCallOutcome.AlreadyExists);
        conflictHandler.Requests.Should().ContainSingle();

        var (client, handler, _) = Create(Always(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var result = await client.StartAsync(Thread, 42);

        result.Outcome.Should().Be(AgentCallOutcome.Unavailable);
        result.Detail.Should().Be("HTTP 503");
        handler.Requests.Should().ContainSingle("a POST is never retried");
    }

    [Fact]
    public async Task Get_parses_the_real_awaiting_approval_view_including_string_money()
    {
        var (client, handler, _) = Create(Always(() => Json(HttpStatusCode.OK, AgentFixtures.Json(AgentFixtures.AwaitingStudent))));

        var result = await client.GetAsync(Thread);

        result.Outcome.Should().Be(AgentCallOutcome.Ok);
        handler.Requests.Single().Path.Should().Be($"/workflows/{Thread}");
        var view = result.Value!;
        view.Status.Should().Be(AgentWorkflowStatuses.AwaitingApproval);
        view.Validation.Should().HaveCount(12).And.OnlyContain(v => v.Passed && v.Attempt == 1);
        view.Steps!.Select(s => s.Sequence).Should().Equal(1, 2, 3, 4, 5);
        view.Steps!.SelectMany(s => s.ToolCalls ?? []).Should().NotBeEmpty();
        view.PolicySnapshot.Should().NotBeNull();
        var proposal = view.ReadProposal()!;
        proposal.RoomId.Should().Be(1);
        proposal.RoomCode.Should().Be("A301");
        proposal.Equipment!.Lines.Should().Contain(new AgentEquipmentLine("MIC-WIRELESS", 2, "portable"))
            .And.Contain(new AgentEquipmentLine("PROJ-PORTABLE", 0, AgentEquipmentLine.RoomBuiltin));
        proposal.Quote!.Total.Should().Be(5500.00m);
        proposal.Quote.Lines.Sum(l => l.LineTotal).Should().Be(5500.00m);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, AgentCallOutcome.NotFound)]
    [InlineData(HttpStatusCode.Conflict, AgentCallOutcome.Conflict)]
    public async Task Resume_maps_404_and_409(HttpStatusCode status, AgentCallOutcome expected)
    {
        var (client, handler, _) = Create(Always(() => new HttpResponseMessage(status)));

        (await client.ResumeAsync(Thread, AgentDecisions.Cancel, null)).Outcome.Should().Be(expected);
        handler.Requests.Single().Path.Should().Be($"/workflows/{Thread}/resume");
    }

    [Fact]
    public async Task Get_retries_5xx_at_most_twice_then_reports_unavailable()
    {
        var (client, handler, _) = Create(Always(() => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var result = await client.GetAsync(Thread);

        result.Outcome.Should().Be(AgentCallOutcome.Unavailable);
        handler.Requests.Should().HaveCount(1 + AgentClient.GetRetryDelays.Count);
    }

    [Fact]
    public async Task Get_recovers_when_a_retry_succeeds()
    {
        var calls = 0;
        var (client, _, _) = Create(Always(() => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(HttpStatusCode.OK, AgentFixtures.Json(AgentFixtures.Failed))));

        var result = await client.GetAsync(Thread);

        result.Outcome.Should().Be(AgentCallOutcome.Ok);
        result.Value!.Error.Should().StartWith("policy unavailable");
    }

    [Fact]
    public async Task Timeout_and_connection_refused_are_unavailable()
    {
        var (slow, _, _) = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Accepted();
        }, timeout: TimeSpan.FromMilliseconds(100));
        (await slow.StartAsync(Thread, 1)).Should().Be(AgentCallResult<AgentWorkflowAccepted>.Unavailable("timeout"));

        var (down, _, _) = Create((_, _) => throw new HttpRequestException("Connection refused", null, HttpStatusCode.ServiceUnavailable));
        (await down.StartAsync(Thread, 1)).Detail.Should().Be("network error");
    }

    [Fact]
    public async Task The_callers_own_cancellation_still_throws()
    {
        var (client, _, _) = Create(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Accepted();
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => client.StartAsync(Thread, 1, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_rejected_key_is_logged_as_an_error_naming_the_setting_and_is_not_retried()
    {
        var (client, handler, logs) = Create(Always(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var result = await client.GetAsync(Thread);

        result.Should().Be(AgentCallResult<AgentWorkflowView>.Unavailable("HTTP 401"));
        handler.Requests.Should().ContainSingle("a 401 will not change on a retry");
        logs.Entries.Should().Contain(e => e.Message == "Agent service rejected the request: HTTP 401, check AgentService:ServiceKey");
    }

    [Fact]
    public async Task Logs_never_contain_the_key_or_the_notes()
    {
        const string notes = "private-officer-notes-xyz";
        var (client, _, logs) = Create(Always(Accepted));

        await client.ResumeAsync(Thread, AgentDecisions.Revise, notes);
        await client.GetAsync(Thread);

        logs.Entries.Should().NotBeEmpty();
        logs.Entries.Should().NotContain(e => e.Message.Contains(ServiceKey) || e.Message.Contains(notes));
    }
}
