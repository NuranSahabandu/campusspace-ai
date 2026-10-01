using Serilog.Events;

namespace CampusSpace.Api.Health;

/// <summary>
/// Request log levels: the platform's liveness pings (<see cref="LivePath"/>, every few seconds on Render) are logged at
/// Verbose, so they don't flood the log; everything else keeps Serilog's defaults (Error for 5xx or an exception,
/// else Information).
/// </summary>
public static class HealthLogLevels
{
    public const string LivePath = "/health/live";

    public static LogEventLevel ForRequest(HttpContext context, double elapsedMs, Exception? exception) =>
        exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
        : context.Request.Path.Equals(LivePath, StringComparison.OrdinalIgnoreCase) ? LogEventLevel.Verbose
        : LogEventLevel.Information;
}
