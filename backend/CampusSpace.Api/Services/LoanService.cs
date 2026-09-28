using System.Data;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Loans;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class LoanService(
    AppDbContext db,
    IPolicySettingsService policy,
    IDamagePhotoStore photos,
    ICurrentUser currentUser,
    TimeProvider clock) : ILoanService
{
    public const string BookingMissingMessage = "Booking does not exist.";
    public const string ItemMissingMessage = "Equipment item does not exist.";
    public const string ItemDamagedMessage = "Item is damaged";
    public const string BookingEndedMessage = "The booking has ended";
    public const string AlreadyCheckedInMessage = "Loan is already checked in";
    public const string DamagedNoteMessage = "A note is required for a damaged return.";
    public const string DamagedPhotoMessage = "A photo is required for a damaged return.";

    public static string ItemNotAvailableMessage(string status) => $"Item is not available ({status})";
    public static string BookingNotActiveMessage(string status) => $"Booking is not active ({status})";
    public static string NotReservedMessage(string typeCode) => $"This booking has no reservation for {typeCode}";
    public static string AllOutMessage(int reserved, string typeCode) => $"All {reserved} reserved {typeCode} are already out";

    public static string TooEarlyMessage(DateTime opensAtUtc, int windowMinutes) =>
        $"Checkout opens at {new DateTimeOffset(opensAtUtc).ToOffset(CampusTime.Offset):HH:mm} ({windowMinutes} minutes before the start)";

    private long CallerId => currentUser.UserId
        ?? throw new InvalidOperationException("Loans need an authenticated user.");

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<HandoverDto>> TodayAsync(CancellationToken ct = default)
    {
        var today = CampusTime.Today(clock);
        var day = CampusTime.UtcRange(CampusTime.StartOf(today), CampusTime.StartOf(today.AddDays(1)));

        var rows = await db.Bookings.AsNoTracking()
            .Where(b => BookingStatuses.Active.Contains(b.Status) && b.TimeRange.Overlaps(day)
                && db.EquipmentReservations.Any(r => r.BookingId == b.Id))
            .OrderBy(b => b.TimeRange.LowerBound).ThenBy(b => b.Id)
            .Select(b => new
            {
                b.Id,
                RoomCode = b.Room.Code,
                b.TimeRange,
                RequesterName = b.Request.Requester.FullName,
                b.Status,
                Lines = db.EquipmentReservations
                    .Where(r => r.BookingId == b.Id)
                    .OrderBy(r => r.Type.Code)
                    .Select(r => new HandoverLineDto(
                        r.TypeId, r.Type.Code, r.Type.Name, r.Quantity,
                        db.EquipmentLoans.Count(l => l.BookingId == b.Id && l.Item.TypeId == r.TypeId && l.CheckedInAt == null),
                        db.EquipmentLoans.Count(l => l.BookingId == b.Id && l.Item.TypeId == r.TypeId && l.CheckedInAt != null)))
                    .ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new HandoverDto(
            r.Id, r.RoomCode, r.TimeRange.LowerBound, r.TimeRange.UpperBound, r.RequesterName, r.Status, r.Lines)).ToList();
    }

    public Task<PagedResult<LoanDto>> ListAsync(LoansQuery query, CancellationToken ct = default)
    {
        var now = Now;
        var loans = db.EquipmentLoans.AsNoTracking();

        if (query.Overdue is true)
            loans = loans.Where(l => l.CheckedInAt == null && l.DueAt < now);
        else if (query.Overdue is false)
            loans = loans.Where(l => l.CheckedInAt != null || l.DueAt >= now);
        if (query.BookingId is { } bookingId)
            loans = loans.Where(l => l.BookingId == bookingId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = query.Search.ToContainsPattern();
            loans = loans.Where(l => EF.Functions.ILike(l.Item.AssetTag, pattern) || EF.Functions.ILike(l.Item.Type.Code, pattern));
        }

        loans = query.Sort == "dueAt"
            ? loans.OrderBy(l => l.DueAt).ThenBy(l => l.Id)
            : loans.OrderByDescending(l => l.DueAt).ThenByDescending(l => l.Id);

        return ToDtos(loans, now).ToPagedResultAsync(query, ct);
    }

    public Task<LoanDto?> GetAsync(long id, CancellationToken ct = default) =>
        ToDtos(db.EquipmentLoans.AsNoTracking().Where(l => l.Id == id), Now).SingleOrDefaultAsync(ct);

    public async Task<LoanDto> CheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var callerId = CallerId;

        // READ COMMITTED, like the approval transaction. The booking lock serialises every checkout (and cancel) of one
        // booking, so the open-loan count below can't be overtaken. Lock order is always booking, then item.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var booking = await db.Bookings
            .FromSql($"""SELECT * FROM "Bookings" WHERE "Id" = {request.BookingId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException(nameof(CheckoutRequest.BookingId), BookingMissingMessage);
        var item = await db.EquipmentItems
            .FromSql($"""SELECT * FROM "EquipmentItems" WHERE "Id" = {request.ItemId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException(nameof(CheckoutRequest.ItemId), ItemMissingMessage);

        // The row lock re-reads the latest committed item, so a checkout that just committed is seen here.
        if (item.Status == EquipmentItemStatuses.OnLoan)
            throw new ConflictException(EquipmentLoanConfiguration.ItemOnLoanMessage);
        if (item.Status != EquipmentItemStatuses.Available)
            throw new ConflictException(ItemNotAvailableMessage(item.Status));
        if (item.Condition == EquipmentConditions.Damaged)
            throw new ConflictException(ItemDamagedMessage);

        // Active, not only Confirmed (plan §9): a CheckedIn booking still holds its room and reservations.
        if (!BookingStatuses.Active.Contains(booking.Status))
            throw new ConflictException(BookingNotActiveMessage(booking.Status));

        var now = Now;
        var window = (await policy.GetAsync(ct)).CheckoutWindowMinutes;
        var opensAt = booking.TimeRange.LowerBound.AddMinutes(-window);
        if (now < opensAt)
            throw new ConflictException(TooEarlyMessage(opensAt, window));
        if (now >= booking.TimeRange.UpperBound)
            throw new ConflictException(BookingEndedMessage);

        var typeCode = await db.EquipmentTypes.Where(t => t.Id == item.TypeId).Select(t => t.Code).SingleAsync(ct);
        var reserved = await db.EquipmentReservations
            .Where(r => r.BookingId == booking.Id && r.TypeId == item.TypeId)
            .Select(r => (int?)r.Quantity)
            .SingleOrDefaultAsync(ct)
            ?? throw new ConflictException(NotReservedMessage(typeCode));
        var open = await db.EquipmentLoans
            .CountAsync(l => l.BookingId == booking.Id && l.Item.TypeId == item.TypeId && l.CheckedInAt == null, ct);
        if (open >= reserved)
            throw new ConflictException(AllOutMessage(reserved, typeCode));

        // IX_EquipmentLoans_ItemId_Open is the guarantee: a second open loan of this item is a 23505 (409).
        var loan = new EquipmentLoan
        {
            BookingId = booking.Id,
            ItemId = item.Id,
            CheckedOutAt = now,
            CheckedOutById = callerId,
            DueAt = booking.TimeRange.UpperBound,
        };
        db.EquipmentLoans.Add(loan);
        item.Status = EquipmentItemStatuses.OnLoan;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return (await GetAsync(loan.Id, ct))!;
    }

    public async Task<LoanDto?> CheckInAsync(long id, CheckInRequest request, CancellationToken ct = default)
    {
        var callerId = CallerId;

        // Everything about the input is checked before the database or the disk is touched.
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var damaged = request.Condition == EquipmentConditions.Damaged;
        if (damaged)
        {
            var errors = new Dictionary<string, string[]>();
            if (note is null)
                errors[nameof(CheckInRequest.Note)] = [DamagedNoteMessage];
            if (request.Photo is null)
                errors[nameof(CheckInRequest.Photo)] = [DamagedPhotoMessage];
            if (errors.Count > 0)
                throw new BusinessRuleException(errors);
        }
        var format = request.Photo is { } upload ? await photos.ValidateAsync(upload, ct) : null;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var loan = await db.EquipmentLoans
            .FromSql($"""SELECT * FROM "EquipmentLoans" WHERE "Id" = {id} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
        if (loan is null)
            return null;
        if (loan.CheckedInAt is not null)
            throw new ConflictException(AlreadyCheckedInMessage);
        var item = await db.EquipmentItems
            .FromSql($"""SELECT * FROM "EquipmentItems" WHERE "Id" = {loan.ItemId} FOR UPDATE""")
            .SingleAsync(ct);

        string? photoName = null;
        try
        {
            if (format is not null)
                photoName = await photos.SaveAsync(request.Photo!, format, ct);

            var now = Now;
            loan.CheckedInAt = now;
            loan.CheckedInById = callerId;
            loan.ReturnCondition = request.Condition;
            loan.DamageNote = note;
            loan.DamagePhotoPath = photoName;
            loan.IsLateReturn = now > loan.DueAt;

            item.Condition = request.Condition;
            item.Status = damaged ? EquipmentItemStatuses.UnderRepair : EquipmentItemStatuses.Available;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            // The row changes rolled back, so the file must not outlive them.
            if (photoName is not null)
                photos.Delete(photoName);
            throw;
        }

        return await GetAsync(id, ct);
    }

    public async Task<StoredPhoto?> GetPhotoAsync(long id, CancellationToken ct = default)
    {
        var name = await db.EquipmentLoans.AsNoTracking().Where(l => l.Id == id).Select(l => l.DamagePhotoPath).SingleOrDefaultAsync(ct);
        return name is null ? null : photos.Open(name);
    }

    private static IQueryable<LoanDto> ToDtos(IQueryable<EquipmentLoan> loans, DateTime now) => loans.Select(l => new LoanDto(
        l.Id, l.BookingId, l.Booking.Room.Code, l.ItemId, l.Item.AssetTag, l.Item.Type.Code,
        l.CheckedOutAt, l.CheckedOutBy.FullName, l.DueAt, l.CheckedInAt, l.CheckedInBy != null ? l.CheckedInBy.FullName : null,
        l.ReturnCondition, l.DamageNote, l.IsLateReturn, l.CheckedInAt == null && l.DueAt < now, l.DamagePhotoPath != null));
}
