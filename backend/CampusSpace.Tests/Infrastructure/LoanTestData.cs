using System.Net.Http.Headers;
using System.Net.Http.Json;
using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Loans for endpoint tests. Shared-database isolation: every <see cref="HandoverAsync"/> call makes its own building and
/// room (so no_room_overlap can never reject two tests booking the same times, such as "now + 10 min"), its own
/// equipment type with a unique code, and its own items. Tests never use seeded rows such as MIC-WIRELESS, so rooms,
/// items and per-type counts can't collide, whatever order the tests run in.
/// </summary>
public static class LoanTestData
{
    public const string CheckoutUrl = "/api/loans/checkout";

    public static string CheckInUrl(long loanId) => $"/api/loans/{loanId}/checkin";

    public static string PhotoUrl(long loanId) => $"/api/loans/{loanId}/photo";

    /// <summary>The first bytes of a JPEG and a PNG: enough for the magic-byte check.</summary>
    public static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    /// <summary>A booking with one reserved type and its items, all new.</summary>
    public sealed record Handover(long RequestId, long BookingId, long RoomId, long TypeId, string TypeCode, IReadOnlyList<long> ItemIds);

    /// <summary>
    /// A new room booked for [start, end) with <paramref name="status"/>, a new type with <paramref name="reserved"/>
    /// reserved for it (0 = no reservation), and <paramref name="items"/> Available items of that type (default: reserved).
    /// </summary>
    public static async Task<Handover> HandoverAsync(
        CustomWebApplicationFactory factory, DateTimeOffset start, DateTimeOffset end, int reserved = 1, int? items = null,
        string status = BookingStatuses.Confirmed, long? requesterId = null)
    {
        var prefix = FacilitiesTestData.UniquePrefix();
        var buildingId = await FacilitiesTestData.CreateBuildingAsync(factory, prefix);
        var roomId = await FacilitiesTestData.CreateRoomAsync(factory, buildingId, prefix + "-R", RoomTypes.SeminarRoom, 30, []);
        var (requestId, bookingId) = await BookingTestData.InsertApprovedBookingAsync(factory, roomId, start, end, status, requesterId);
        var type = await EquipmentTestData.CreateTypeAsync(factory);
        if (reserved > 0)
            await EquipmentTestData.InsertReservationAsync(factory, bookingId, type.Id, reserved);
        var itemIds = new List<long>();
        for (var i = 0; i < (items ?? reserved); i++)
            itemIds.Add((await EquipmentTestData.CreateItemAsync(factory, type.Id)).Id);
        return new Handover(requestId, bookingId, roomId, type.Id, type.Code, itemIds);
    }

    public static Task<(HttpClient Client, long UserId)> TechnicianAsync(CustomWebApplicationFactory factory) =>
        TestAuth.CreateUserClientAsync(factory, Roles.LabTechnician);

    public static Task<HttpResponseMessage> CheckoutAsync(HttpClient client, long bookingId, long itemId) =>
        client.PostAsJsonAsync(CheckoutUrl, new { bookingId, itemId });

    /// <summary>Checks out and returns the new loan's id (asserting 201).</summary>
    public static async Task<long> CheckoutOkAsync(HttpClient client, long bookingId, long itemId)
    {
        var response = await CheckoutAsync(client, bookingId, itemId);
        if (response.StatusCode != System.Net.HttpStatusCode.Created)
            throw new InvalidOperationException($"Checkout failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.ReadJsonAsync()).GetProperty("id").GetInt64();
    }

    /// <summary>A multipart check-in. <paramref name="photo"/> null sends no file part.</summary>
    public static Task<HttpResponseMessage> CheckInAsync(
        HttpClient client, long loanId, string condition, string? note = null, byte[]? photo = null,
        string fileName = "photo.jpg", string contentType = "image/jpeg")
    {
        var form = new MultipartFormDataContent { { new StringContent(condition), "condition" } };
        if (note is not null)
            form.Add(new StringContent(note), "note");
        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "photo", fileName);
        }
        return client.PostAsync(CheckInUrl(loanId), form);
    }

    /// <summary>
    /// Inserts a loan directly (for states checkout refuses to create, such as one already past due). An open loan also
    /// sets the item OnLoan, as checkout would.
    /// </summary>
    public static async Task<long> InsertLoanAsync(
        CustomWebApplicationFactory factory, long bookingId, long itemId, long technicianId, DateTimeOffset checkedOutAt,
        DateTimeOffset dueAt, DateTimeOffset? checkedInAt = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loan = new EquipmentLoan
        {
            BookingId = bookingId, ItemId = itemId, CheckedOutAt = checkedOutAt.UtcDateTime, CheckedOutById = technicianId,
            DueAt = dueAt.UtcDateTime,
        };
        if (checkedInAt is { } at)
        {
            loan.CheckedInAt = at.UtcDateTime;
            loan.CheckedInById = technicianId;
            loan.ReturnCondition = EquipmentConditions.Good;
            loan.IsLateReturn = at > dueAt;
        }
        else
        {
            var item = await db.EquipmentItems.SingleAsync(i => i.Id == itemId);
            item.Status = EquipmentItemStatuses.OnLoan;
        }
        db.EquipmentLoans.Add(loan);
        await db.SaveChangesAsync();
        return loan.Id;
    }

    public static async Task<EquipmentItem> ItemAsync(CustomWebApplicationFactory factory, long itemId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().EquipmentItems.AsNoTracking().SingleAsync(i => i.Id == itemId);
    }

    public static async Task<EquipmentLoan> LoanAsync(CustomWebApplicationFactory factory, long loanId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().EquipmentLoans.AsNoTracking().SingleAsync(l => l.Id == loanId);
    }

    /// <summary>Sets an item's status and condition directly (UnderRepair, Retired, ...).</summary>
    public static async Task SetItemAsync(CustomWebApplicationFactory factory, long itemId, string status, string condition = EquipmentConditions.Good)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.EquipmentItems.SingleAsync(i => i.Id == itemId);
        item.Status = status;
        item.Condition = condition;
        await db.SaveChangesAsync();
    }
}
