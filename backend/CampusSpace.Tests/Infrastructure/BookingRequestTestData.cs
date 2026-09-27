using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Booking request rows for endpoint tests. Every test uses fresh users (TestAuth.CreateUserClientAsync) and clubs with
/// unique names, so open-request counts and "own rows only" never depend on other tests.
/// </summary>
public static class BookingRequestTestData
{
    public const string Url = "/api/booking-requests";

    /// <summary>10:00 campus time, <paramref name="daysAhead"/> days from today.</summary>
    public static DateTimeOffset FutureStart(int daysAhead = 30, int hour = 10) =>
        new(CampusTime.Today(TimeProvider.System).AddDays(daysAhead).ToDateTime(new TimeOnly(hour, 0)), CampusTime.Offset);

    /// <summary>A valid body. Times are sent with +05:30, as Flutter sends them.</summary>
    public static Dictionary<string, object?> Body(
        long? clubId = null, string purpose = "Robotics workshop", int attendees = 45, DateTimeOffset? start = null,
        int hours = 3, decimal budget = 8000m, string[]? features = null, object[]? equipment = null, string? notes = null)
    {
        var s = start ?? FutureStart();
        return new Dictionary<string, object?>
        {
            ["purpose"] = purpose,
            ["attendees"] = attendees,
            ["requestedStart"] = s.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            ["requestedEnd"] = s.AddHours(hours).ToString("yyyy-MM-ddTHH:mm:sszzz"),
            ["budgetLkr"] = budget,
            ["requiredFeatures"] = features ?? [],
            ["equipment"] = equipment ?? [],
            ["clubId"] = clubId,
            ["notes"] = notes,
        };
    }

    public static object Line(long typeId, int quantity) => new { typeId, quantity };

    public static async Task<long> CreateClubAsync(
        CustomWebApplicationFactory factory, long? representativeId = null, bool isActive = true, long? memberId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var club = new Club { Name = $"Club {Guid.NewGuid():N}", IsActive = isActive };
        if (representativeId is { } rep)
            club.Members.Add(new ClubMember { UserId = rep, IsRepresentative = true, JoinedAt = DateTime.UtcNow });
        if (memberId is { } member)
            club.Members.Add(new ClubMember { UserId = member, IsRepresentative = false, JoinedAt = DateTime.UtcNow });
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        return club.Id;
    }

    /// <summary>A new student who represents a new active club.</summary>
    public static async Task<(HttpClient Client, long UserId, long ClubId)> StudentRepAsync(CustomWebApplicationFactory factory)
    {
        var (client, userId) = await TestAuth.CreateUserClientAsync(factory, Roles.Student);
        return (client, userId, await CreateClubAsync(factory, userId));
    }

    /// <summary>Moves a request through the real state machine, one step per target status.</summary>
    public static async Task MoveAsync(CustomWebApplicationFactory factory, long requestId, params string[] path)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var machine = scope.ServiceProvider.GetRequiredService<IRequestStateMachine>();
        var request = await db.BookingRequests.SingleAsync(r => r.Id == requestId);
        foreach (var to in path)
            machine.Transition(request, to, changedById: null, reason: "test");
        await db.SaveChangesAsync();
    }
}
