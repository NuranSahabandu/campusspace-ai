using System.Diagnostics;
using CampusSpace.Api.Data;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Services;

namespace CampusSpace.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public const string FrontendsCorsPolicy = "Frontends";

    /// <summary>Problem Details (RFC 9457) for every error response, each with a traceId, plus the global exception handler.</summary>
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    /// <summary>CORS for the React app. Origins come from Cors:AllowedOrigins.</summary>
    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(FrontendsCorsPolicy, policy =>
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
        return services;
    }

    /// <summary>Business services (Controller -> IService -> AppDbContext). One scope per request.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        return services;
    }

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");
        // Phase 0.5: add the agent-service check (GET {AgentService:BaseUrl}/health).
        return services;
    }
}
