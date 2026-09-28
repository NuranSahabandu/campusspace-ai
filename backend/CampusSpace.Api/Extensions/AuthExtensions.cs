using System.Text;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Options;
using CampusSpace.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CampusSpace.Api.Extensions;

public static class AuthExtensions
{
    /// <summary>
    /// JWT bearer authentication plus default-deny authorization: every endpoint needs a valid token
    /// unless it is marked [AllowAnonymous].
    /// </summary>
    public static IServiceCollection AddJwtAuth(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => Encoding.UTF8.GetByteCount(o.Key ?? string.Empty) >= JwtOptions.MinKeyBytes,
                JwtOptions.KeyTooShortMessage)
            .ValidateOnStart();

        services.AddOptions<AgentToolsOptions>()
            .BindConfiguration(AgentToolsOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => Encoding.UTF8.GetByteCount(o.Key ?? string.Empty) >= AgentToolsOptions.MinKeyBytes,
                AgentToolsOptions.KeyTooShortMessage)
            .ValidateOnStart();

        // Bearer stays the default scheme; AgentKey runs only where the AgentTools policy names it.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(AgentKeyDefaults.Scheme, null);

        // Configured when the options are first resolved, so test UseSetting overrides are picked up.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                // Explicit claim names: no Microsoft URI mapping, "sub"/"name"/"role" stay as issued.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtClaimNames.Name,
                    RoleClaimType = JwtClaimNames.Role,
                };
            });

        services.AddAuthorization(options =>
        {
            // JWT only, named explicitly: the agent principal can never satisfy [Authorize] or the fallback on /api.
            var jwtUser = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build();
            options.DefaultPolicy = jwtUser;
            options.FallbackPolicy = jwtUser;
            // /internal/agent-tools only: the X-Agent-Key scheme and nothing else, so a user JWT gets a 401 there.
            options.AddPolicy(AgentKeyDefaults.Policy, policy => policy
                .AddAuthenticationSchemes(AgentKeyDefaults.Scheme)
                .RequireClaim(AgentKeyDefaults.ClaimType, AgentKeyDefaults.ClaimValue));
        });

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITokenService, TokenService>();
        return services;
    }

    /// <summary>Keeps /internal (the agent tools, X-Agent-Key) out of the public Swagger document (§9).</summary>
    public static void HideInternalRoutes(this SwaggerGenOptions options) =>
        options.DocInclusionPredicate((_, api) =>
            !(api.RelativePath ?? string.Empty).StartsWith("internal/", StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the "Bearer" scheme so Swagger UI shows the Authorize button (Lab 04 Task 04).</summary>
    public static void AddBearerSecurity(this SwaggerGenOptions options)
    {
        var scheme = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Description = "Paste the accessToken from /api/auth/login (without the 'Bearer ' prefix).",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
        };
        options.AddSecurityDefinition("Bearer", scheme);
        options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
    }
}
