using CampusSpace.Api.Extensions;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Notifications;

public static class NotificationServiceExtensions
{
    /// <summary>
    /// Email notifications (plan §14): validated options, the Brevo typed HttpClient (api-key header, 10 s timeout, header
    /// logging redacted), the no-op sender when no key is set, the per-row delivery and the outbox dispatcher.
    /// </summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<BrevoEmailSender>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
                client.BaseAddress = AgentServiceExtensions.BaseAddress(options.BaseUrl);
                client.Timeout = BrevoEmailSender.Timeout;
                if (options.IsConfigured)
                    client.DefaultRequestHeaders.Add(BrevoEmailSender.ApiKeyHeader, options.BrevoApiKey!.Trim());
            })
            // IHttpClientFactory's own logging must never print the key.
            .RedactLoggedHeaders(_ => true);
        services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<IOptions<EmailOptions>>().Value.IsConfigured
            ? sp.GetRequiredService<BrevoEmailSender>()
            : new NoOpEmailSender());

        services.AddScoped<INotificationDelivery, NotificationDelivery>();
        services.AddScoped<INotificationLogService, NotificationLogService>();
        // One instance, so tests can resolve it and call ProcessOnceAsync; ExecuteAsync returns at once when it is disabled.
        services.AddSingleton<NotificationDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());
        return services;
    }
}
