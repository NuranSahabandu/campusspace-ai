using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CampusSpace.Api.Agents;

/// <summary>
/// Typed HttpClient for the agent service. BaseAddress, the 10 s timeout and the X-Service-Key header are set where it
/// is registered (AgentServiceExtensions). Each call logs method, route pattern, status and duration: never the key,
/// a body, or the officer's notes. Only GET is retried (plan §6: never retry a non-idempotent POST).
/// </summary>
public sealed class AgentClient(HttpClient http, ILogger<AgentClient> logger) : IAgentClient
{
    public const string ServiceKeyHeader = "X-Service-Key";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Backoff before each GET retry: at most two retries.</summary>
    public static readonly IReadOnlyList<TimeSpan> GetRetryDelays = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)];

    private const string StartRoute = "/workflows";
    private const string ViewRoute = "/workflows/{thread_id}";
    private const string ResumeRoute = "/workflows/{thread_id}/resume";

    public Task<AgentCallResult<AgentWorkflowAccepted>> StartAsync(Guid threadId, long requestId, CancellationToken ct = default) =>
        SendAsync<AgentWorkflowAccepted>(
            () => new HttpRequestMessage(HttpMethod.Post, "workflows")
            {
                Content = JsonContent.Create(new StartWorkflowBody(threadId, requestId), options: AgentJson.Options),
            },
            StartRoute, conflict: AgentCallOutcome.AlreadyExists, retryDelays: [], ct);

    public Task<AgentCallResult<AgentWorkflowView>> GetAsync(Guid threadId, CancellationToken ct = default) =>
        SendAsync<AgentWorkflowView>(
            () => new HttpRequestMessage(HttpMethod.Get, $"workflows/{threadId}"),
            ViewRoute, conflict: AgentCallOutcome.Conflict, GetRetryDelays, ct);

    public Task<AgentCallResult<AgentWorkflowAccepted>> ResumeAsync(
        Guid threadId, string decision, string? notes, CancellationToken ct = default) =>
        SendAsync<AgentWorkflowAccepted>(
            () => new HttpRequestMessage(HttpMethod.Post, $"workflows/{threadId}/resume")
            {
                Content = JsonContent.Create(new ResumeWorkflowBody(decision, notes), options: AgentJson.Options),
            },
            ResumeRoute, conflict: AgentCallOutcome.Conflict, retryDelays: [], ct);

    private async Task<AgentCallResult<T>> SendAsync<T>(
        Func<HttpRequestMessage> build, string route, AgentCallOutcome conflict, IReadOnlyList<TimeSpan> retryDelays,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var result = await SendOnceAsync<T>(build, route, conflict, ct);
            // Only outages are retried: a 4xx or a parsed answer will not change on a second try.
            if (result.Outcome != AgentCallOutcome.Unavailable || attempt >= retryDelays.Count || !IsTransient(result.Detail))
                return result;
            await Task.Delay(retryDelays[attempt], ct);
        }
    }

    private static bool IsTransient(string? detail) =>
        detail is "timeout" or "network error" || detail?.StartsWith("HTTP 5", StringComparison.Ordinal) == true;

    private async Task<AgentCallResult<T>> SendOnceAsync<T>(
        Func<HttpRequestMessage> build, string route, AgentCallOutcome conflict, CancellationToken ct)
    {
        using var request = build();
        var method = request.Method.Method;
        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout surfaces as a cancellation the caller did not ask for.
            logger.LogWarning("Agent service {Method} {Route} timed out after {ElapsedMs} ms", method, route, Elapsed(started));
            return AgentCallResult<T>.Unavailable("timeout");
        }
        catch (HttpRequestException ex)
        {
            // Only the error type: the message can carry the URL, never the key, but stays out of logs anyway.
            logger.LogWarning("Agent service {Method} {Route} failed: {ErrorType} after {ElapsedMs} ms",
                method, route, ex.HttpRequestError, Elapsed(started));
            return AgentCallResult<T>.Unavailable("network error");
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            logger.LogInformation("Agent service {Method} {Route} returned {StatusCode} in {ElapsedMs} ms",
                method, route, status, Elapsed(started));

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return new AgentCallResult<T>(AgentCallOutcome.NotFound, default, "HTTP 404");
                case HttpStatusCode.Conflict:
                    return new AgentCallResult<T>(conflict, default, "HTTP 409");
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    logger.LogError(
                        "Agent service rejected the request: HTTP {StatusCode}, check AgentService:ServiceKey", status);
                    return AgentCallResult<T>.Unavailable($"HTTP {status}");
                case >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError:
                    logger.LogError(
                        "Agent service rejected the request: HTTP {StatusCode}, the body does not match the /workflows contract",
                        status);
                    return AgentCallResult<T>.Unavailable($"HTTP {status}");
                case >= HttpStatusCode.InternalServerError:
                    return AgentCallResult<T>.Unavailable($"HTTP {status}");
            }

            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(AgentJson.Options, ct);
                return value is null
                    ? AgentCallResult<T>.Unavailable("empty response")
                    : new AgentCallResult<T>(AgentCallOutcome.Ok, value);
            }
            catch (JsonException)
            {
                logger.LogError("Agent service {Method} {Route} returned a body that does not match the contract", method, route);
                return AgentCallResult<T>.Unavailable("invalid response");
            }
        }
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
