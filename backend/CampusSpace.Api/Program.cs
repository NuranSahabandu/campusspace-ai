using CampusSpace.Api.Extensions;
using CampusSpace.Api.Health;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Notifications;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Sinks, formatters and levels come from the "Serilog" section of appsettings.*.json.
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

// Render sets PORT; TLS ends at its proxy.
builder.UsePlatformPort();

builder.Services.AddPersistence();
builder.Services.AddErrorHandling();
builder.Services.AddProxySupport();
builder.Services.AddFrontendCors(builder.Configuration, builder.Environment);
builder.Services.AddApiHealthChecks();
builder.Services.AddJwtAuth();
builder.Services.AddApplicationServices();
builder.Services.AddAgentService();
builder.Services.AddNotifications();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddBearerSecurity();
    options.HideInternalRoutes();
});

var app = builder.Build();

// Behind Render's proxy: the client's scheme and address, before anything reads them (HTTPS redirection, logs).
if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
    app.UseForwardedHeaders();
// Then, so it logs the final status of every agent-tool call, including 401s and handled exceptions.
app.UseAgentToolRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

// The spec needs a public Swagger URL in Production (Swagger:Enabled); internal agent-tool routes stay hidden.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
// Local dev is HTTP only (port 5080); TLS is enforced outside Development.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirectionExceptHealth();

// Migrations only where Database:MigrateOnStartup is on (Development); the seed where Seed:OnStartup is on.
await app.PrepareDatabaseAsync();

app.UseCors(ServiceCollectionExtensions.FrontendsCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync })
    .AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;
