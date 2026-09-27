using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Bookings for tests. No endpoint creates bookings yet (the Phase 3 approval transaction will), so these insert them
/// directly, each for a new request moved to Approved through the real state machine.
/// </summary>
public static class BookingTestData
{
    /// <summary>[from, to) on a campus date, for example CampusSlot(date, "14:00", "17:00").</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) CampusSlot(DateOnly date, string from, string to) =>
        (CampusTime.At(date, TimeOnly.Parse(from)), CampusTime.At(date, TimeOnly.Parse(to)));

    /// <summary>A new lecturer's request for [start, end), moved to Approved. Returns its id.</summary>
    public static async Task<long> CreateApprovedRequestAsync(CustomWebApplicationFactory factory, DateTimeOffset start, DateTimeOffset end)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var machine = scope.ServiceProvider.GetRequiredService<IRequestStateMachine>();

        var requester = new User
        {
            FullName = $"Requester {Guid.NewGuid():N}"[..40],
            Email = $"{Guid.NewGuid():N}@campus.test",
            PasswordHash = "not-a-real-hash",
            Role = Roles.Lecturer,
        };
        var request = new BookingRequest
        {
            Requester = requester,
            Purpose = "Secret purpose " + Guid.NewGuid().ToString("N"),
            Attendees = 10,
            RequestedStart = start.UtcDateTime,
            RequestedEnd = end.UtcDateTime,
            BudgetLkr = 0,
        };
        machine.Start(request, changedById: null);
        foreach (var to in new[] { RequestStatuses.AgentProcessing, RequestStatuses.PendingApproval, RequestStatuses.Approved })
            machine.Transition(request, to, changedById: null, reason: "test");
        db.BookingRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    /// <summary>Inserts a booking of <paramref name="roomId"/> for [start, end) and returns its id.</summary>
    public static async Task<long> InsertBookingAsync(
        CustomWebApplicationFactory factory, long roomId, DateTimeOffset start, DateTimeOffset end,
        string status = BookingStatuses.Confirmed)
    {
        var requestId = await CreateApprovedRequestAsync(factory, start, end);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var booking = new Booking { RequestId = requestId, RoomId = roomId, TimeRange = CampusTime.UtcRange(start, end), Status = status };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }
}
