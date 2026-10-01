using Microsoft.AspNetCore.HttpOverrides;

namespace CampusSpace.Api.Extensions;

/// <summary>Hosting behind a platform proxy (Render): the PORT variable, forwarded headers, HTTPS redirection, CORS checks.</summary>
public static class HostingExtensions
{
    /// <summary>
    /// The URL to listen on when the platform sets PORT (Render does), else null and the usual configuration applies
    /// (launchSettings: http://localhost:5080). TLS ends at the platform's proxy, so this is plain HTTP on all interfaces.
    /// </summary>
    public static string? UrlForPort(string? port)
    {
        if (string.IsNullOrWhiteSpace(port))
            return null;
        return int.TryParse(port, out var value) && value is >= 1 and <= 65535
            ? $"http://0.0.0.0:{value}"
            : throw new InvalidOperationException("PORT must be a number from 1 to 65535.");
    }

    public static WebApplicationBuilder UsePlatformPort(this WebApplicationBuilder builder)
    {
        if (UrlForPort(builder.Configuration["PORT"]) is { } url)
            builder.WebHost.UseUrls(url);
        return builder;
    }

    /// <summary>
    /// X-Forwarded-For and X-Forwarded-Proto from the platform's proxy (used only when ForwardedHeaders:Enabled is on).
    /// Render's proxy addresses are not fixed, so no known proxies are listed; ForwardLimit 1 takes only the last entry,
    /// the one Render's own proxy appended, so a client can't spoof its address or scheme by sending these headers.
    /// </summary>
    public static IServiceCollection AddProxySupport(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });
        return services;
    }

    /// <summary>
    /// HTTPS redirection outside Development, except for /health: the platform's own health check calls it over plain
    /// HTTP from inside its network, and a redirect would read as a failure.
    /// </summary>
    public static WebApplication UseHttpsRedirectionExceptHealth(this WebApplication app)
    {
        app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection());
        return app;
    }

    /// <summary>
    /// Production CORS rules (plan §15.3: the deployed React origin and the localhost dev origin only): at least one
    /// origin, no wildcard, each an origin (scheme://host[:port], no path), https unless it is localhost.
    /// </summary>
    public static IReadOnlyList<string> CorsProblems(IReadOnlyList<string> origins)
    {
        if (origins.Count == 0)
            return ["Cors:AllowedOrigins is empty (set Cors__AllowedOrigins__0, __1, ...)."];
        var problems = new List<string>();
        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || origin.Contains('*')
                || uri.GetLeftPart(UriPartial.Authority) != origin.TrimEnd('/'))
                problems.Add($"Cors origin \"{origin}\" is not a single origin (scheme://host[:port]).");
            else if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
                problems.Add($"Cors origin \"{origin}\" must use https (only localhost may use http).");
        }
        return problems;
    }
}
