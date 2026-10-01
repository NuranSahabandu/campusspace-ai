using CampusSpace.Api.Models;
using CampusSpace.Api.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Email tests (Task 5.2). The factory's own dispatcher uses the no-op sender (no key → Skipped). <see cref="WithEmail"/>
/// is the same API and database with a fake sender (and optionally Email:RedirectAllTo); dispose it after the test.
/// </summary>
public static class NotificationTestData
{
    public static WebApplicationFactory<Program> WithEmail(CustomWebApplicationFactory factory, IEmailSender sender, string? redirect = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (redirect is not null)
                builder.UseSetting("Email:RedirectAllTo", redirect);
            builder.ConfigureTestServices(services => services.AddScoped(_ => sender));
        });

    public static NotificationDispatcher Dispatcher(this WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<NotificationDispatcher>();

    public static Task<List<NotificationLog>> NotificationsAsync(this AgentPollerEnv env, long requestId) =>
        env.QueryAsync(db => db.NotificationLogs.AsNoTracking().Include(n => n.StatusHistory)
            .Where(n => n.RequestId == requestId).OrderBy(n => n.Id).ToListAsync());

    public static Task<string> RequesterEmailAsync(this AgentPollerEnv env, long requestId) =>
        env.QueryAsync(db => db.BookingRequests.Where(r => r.Id == requestId).Select(r => r.Requester.Email).SingleAsync());
}
