using System.Diagnostics;
using CampusSpace.Api.Auth;

namespace CampusSpace.Api.Middleware;

/// <summary>
/// One log line per /internal/agent-tools call: method, route pattern, status and duration. It runs before routing and
/// authentication, so it also sees 401s and the final status of handled exceptions. It never logs headers (the key)
/// or the query string: the route falls back to the path only.
/// </summary>
public static class AgentToolRequestLogging
{
    public static IApplicationBuilder UseAgentToolRequestLogging(this IApplicationBuilder app) =>
        app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments(AgentKeyDefaults.RoutePrefix, StringComparison.OrdinalIgnoreCase),
            branch => branch.Use(async (context, next) =>
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    await next(context);
                }
                finally
                {
                    var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value;
                    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AgentToolRequestLogging))
                        .LogInformation("Agent tool call {Method} {Route} returned {StatusCode} in {ElapsedMs} ms",
                            context.Request.Method, route, context.Response.StatusCode,
                            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }));
}
