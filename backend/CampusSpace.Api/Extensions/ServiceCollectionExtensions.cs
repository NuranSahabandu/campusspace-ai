using System.Diagnostics;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Health;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Options;
using CampusSpace.Api.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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
        services.AddHttpContextAccessor();
        // "Today" for pricing and policy rules. Also registered by AddJwtAuth; TryAdd keeps one instance either way.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IClubService, ClubService>();
        services.AddScoped<IBuildingService, BuildingService>();
        services.AddScoped<IFeatureService, FeatureService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IRoomBlackoutService, RoomBlackoutService>();
        services.AddScoped<IRoomAvailabilityService, RoomAvailabilityService>();
        services.AddScoped<IEquipmentAvailabilityService, EquipmentAvailabilityService>();
        services.AddScoped<IEquipmentTypeService, EquipmentTypeService>();
        services.AddScoped<IEquipmentItemService, EquipmentItemService>();
        services.AddScoped<ILoanService, LoanService>();
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IDamagePhotoStore>(sp => new DamagePhotoStore(Path.Combine(
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
            sp.GetRequiredService<IOptions<StorageOptions>>().Value.DamagePhotosPath)));
        services.AddScoped<IPricingRuleService, PricingRuleService>();
        services.AddScoped<IQuotationCalculator, QuotationCalculator>();
        services.AddScoped<IQuotationService, QuotationService>();
        services.AddScoped<IPolicySettingsService, PolicySettingsService>();
        services.AddScoped<IBookingWindowRules, BookingWindowRules>();
        services.AddScoped<IRequestStateMachine, RequestStateMachine>();
        services.AddScoped<IBookingRequestService, BookingRequestService>();
        services.AddScoped<IAgentToolService, AgentToolService>();
        services.AddScoped<IApprovalFinalizer, ApprovalFinalizer>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<IApprovalQueueService, ApprovalQueueService>();
        services.AddScoped<IAgentRunReadService, AgentRunReadService>();
        return services;
    }

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHttpClient(AgentServiceHealthCheck.ClientName, (sp, client) =>
        {
            client.BaseAddress = AgentServiceExtensions.BaseAddress(sp.GetRequiredService<IOptions<AgentServiceOptions>>().Value.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(3);
        });

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database")
            // Degraded, not Unhealthy: the API still serves everything except agent runs.
            .AddCheck<AgentServiceHealthCheck>("agent-service", failureStatus: HealthStatus.Degraded);
        return services;
    }
}
