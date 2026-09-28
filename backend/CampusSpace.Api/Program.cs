using CampusSpace.Api.Extensions;
using CampusSpace.Api.Health;
using CampusSpace.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Sinks, formatters and levels come from the "Serilog" section of appsettings.*.json.
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

builder.Services.AddPersistence();
builder.Services.AddErrorHandling();
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddApiHealthChecks();
builder.Services.AddJwtAuth();
builder.Services.AddApplicationServices();
builder.Services.AddAgentService();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddBearerSecurity();
    options.HideInternalRoutes();
});

var app = builder.Build();

// First, so it logs the final status of every agent-tool call, including 401s and handled exceptions.
app.UseAgentToolRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    await app.MigrateAndSeedAsync();
}
else
{
    // Local dev is HTTP only (port 5080); TLS is enforced outside Development.
    app.UseHttpsRedirection();
}

app.UseCors(ServiceCollectionExtensions.FrontendsCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync })
    .AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;
