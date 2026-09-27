using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Requests;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class BookingRequestService(
    AppDbContext db,
    ICurrentUser currentUser,
    IPolicySettingsService policy,
    IRequestStateMachine stateMachine,
    TimeProvider clock) : IBookingRequestService
{
    public const string NotRepresentativeMessage = "You must be the registered representative of an active club";
    public const string LecturerClubMessage = "Lecturer bookings are academic and can't name a club";

    private long CallerId => currentUser.UserId
        ?? throw new InvalidOperationException("Booking requests need an authenticated user.");

    private bool CallerIsOfficer => currentUser.IsInRole(Roles.FacilitiesOfficer);

    public async Task<BookingRequestDetailDto> CreateAsync(CreateBookingRequestRequest request, CancellationToken ct = default)
    {
        var requesterId = CallerId;
        var errors = new Dictionary<string, List<string>>();

        var purpose = request.Purpose.Trim();
        if (purpose.Length == 0)
            AddError(errors, nameof(request.Purpose), "Purpose is required.");

        var start = request.RequestedStart!.Value;
        var end = request.RequestedEnd!.Value;
        if (start <= clock.GetUtcNow())
            AddError(errors, nameof(request.RequestedStart), "Start must be in the future.");
        // TODO(Phase 2): lead time, opening hours, granularity, max duration and advance window (V05/V06), read
        // through IPolicySettingsService.

        // numeric(10,2) would silently round a third decimal place, so reject it instead.
        var budget = request.BudgetLkr!.Value;
        if (decimal.Round(budget, 2) != budget)
            AddError(errors, nameof(request.BudgetLkr), "Budget can have at most 2 decimal places.");

        var features = await CheckFeaturesAsync(request.RequiredFeatures, errors, ct);
        var lines = await CheckEquipmentAsync(request.Equipment, errors, ct);
        await CheckClubAsync(requesterId, request.ClubId, errors, ct);

        if (errors.Count > 0)
            throw new BusinessRuleException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

        // The cap check and the insert must be atomic per requester: otherwise two submits sent at the same moment
        // both count N-1 open requests and both pass. The transaction-scoped advisory lock (keyed by the requester's
        // id) makes a second submit by the same user wait until the first commits, so it counts the new row.
        // Other users are not blocked. SaveChangesAsync reuses this transaction for the request and its audit rows.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({requesterId})", ct);

        var open = await CountOpenAsync(requesterId, ct);
        var max = (await policy.GetAsync(ct)).MaxOpenRequests;
        if (open >= max)
            throw new ConflictException(CapMessage(open, max));

        var entity = new BookingRequest
        {
            RequesterId = requesterId,
            ClubId = request.ClubId,
            Purpose = purpose,
            Attendees = request.Attendees,
            RequestedStart = start.UtcDateTime,
            RequestedEnd = end.UtcDateTime,
            BudgetLkr = budget,
            RequiredFeatures = features,
            // Untrusted text: stored as given, never interpreted or logged.
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes,
            EquipmentLines = lines,
        };
        stateMachine.Start(entity, requesterId);
        db.BookingRequests.Add(entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Phase 3: create AgentRun #1, move to AgentProcessing, start the workflow and return 202 instead of 201.
        return (await LoadDetailAsync(entity.Id, ct))!;
    }

    public Task<PagedResult<BookingRequestSummaryDto>> ListAsync(BookingRequestsQuery query, CancellationToken ct = default)
    {
        var requests = db.BookingRequests.AsNoTracking();
        if (!CallerIsOfficer)
        {
            var callerId = CallerId;
            requests = requests.Where(r => r.RequesterId == callerId);
        }

        var statuses = query.Statuses();
        if (statuses.Count > 0)
            requests = requests.Where(r => statuses.Contains(r.Status));
        if (query.From is { } from)
        {
            var fromUtc = CampusDayStartUtc(from);
            requests = requests.Where(r => r.RequestedStart >= fromUtc);
        }
        if (query.To is { } to)
        {
            var toUtc = CampusDayStartUtc(to.AddDays(1));
            requests = requests.Where(r => r.RequestedStart < toUtc);
        }
        if (query.ClubId is { } clubId)
            requests = requests.Where(r => r.ClubId == clubId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = query.Search.ToContainsPattern();
            requests = CallerIsOfficer
                ? requests.Where(r => EF.Functions.ILike(r.Purpose, pattern)
                    || EF.Functions.ILike(r.Requester.FullName, pattern)
                    || EF.Functions.ILike(r.Requester.Email, pattern)
                    || (r.Club != null && EF.Functions.ILike(r.Club.Name, pattern)))
                : requests.Where(r => EF.Functions.ILike(r.Purpose, pattern));
        }

        requests = query.Sort switch
        {
            "createdAt" => requests.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id),
            "requestedStart" => requests.OrderBy(r => r.RequestedStart).ThenBy(r => r.Id),
            "-requestedStart" => requests.OrderByDescending(r => r.RequestedStart).ThenBy(r => r.Id),
            "status" => requests.OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt).ThenBy(r => r.Id),
            "-status" => requests.OrderByDescending(r => r.Status).ThenByDescending(r => r.CreatedAt).ThenBy(r => r.Id),
            "attendees" => requests.OrderBy(r => r.Attendees).ThenBy(r => r.Id),
            "-attendees" => requests.OrderByDescending(r => r.Attendees).ThenBy(r => r.Id),
            _ => requests.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
        };

        return requests.Select(r => new BookingRequestSummaryDto(
                r.Id, r.Purpose, r.Status, r.RequestedStart, r.RequestedEnd, r.Attendees, r.BudgetLkr,
                r.Club != null ? r.Club.Name : null, r.Requester.FullName, r.CreatedAt))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<BookingRequestDetailDto?> GetAsync(long id, CancellationToken ct = default) =>
        await EnsureCanReadAsync(id, ct) ? await LoadDetailAsync(id, ct) : null;

    public async Task<IReadOnlyList<RequestStatusHistoryDto>?> GetHistoryAsync(long id, CancellationToken ct = default) =>
        await EnsureCanReadAsync(id, ct) ? await HistoryQuery(id).ToListAsync(ct) : null;

    public async Task<EligibilityDto> GetEligibilityAsync(CancellationToken ct = default)
    {
        var callerId = CallerId;
        var isStudent = currentUser.IsInRole(Roles.Student);
        var clubs = isStudent
            ? await db.ClubMembers.AsNoTracking()
                .Where(m => m.UserId == callerId && m.IsRepresentative && m.Club.IsActive)
                .OrderBy(m => m.Club.Name)
                .Select(m => new ClubRefDto(m.ClubId, m.Club.Name))
                .ToListAsync(ct)
            : [];
        var open = await CountOpenAsync(callerId, ct);
        var max = (await policy.GetAsync(ct)).MaxOpenRequests;

        var reason = isStudent && clubs.Count == 0 ? NotRepresentativeMessage
            : open >= max ? CapMessage(open, max)
            : null;
        return new EligibilityDto(reason is null, reason, clubs, open, max, ClubRequired: isStudent);
    }

    private static string CapMessage(int open, int max) =>
        $"You already have {open} open requests (the limit is {max})";

    private static DateTime CampusDayStartUtc(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), CampusTime.Offset).UtcDateTime;

    private Task<int> CountOpenAsync(long requesterId, CancellationToken ct) =>
        db.BookingRequests.CountAsync(r => r.RequesterId == requesterId && RequestStatuses.Open.Contains(r.Status), ct);

    /// <summary>False when the request doesn't exist. Throws ForbiddenException for a requester who isn't the owner.</summary>
    private async Task<bool> EnsureCanReadAsync(long id, CancellationToken ct)
    {
        var ownerId = await db.BookingRequests.Where(r => r.Id == id).Select(r => (long?)r.RequesterId).SingleOrDefaultAsync(ct);
        if (ownerId is null)
            return false;
        // Object-level check (§15.1): the role check alone would let any student read any request.
        if (!CallerIsOfficer && ownerId != CallerId)
            throw new ForbiddenException("You can only view your own requests");
        return true;
    }

    private IQueryable<RequestStatusHistoryDto> HistoryQuery(long id) => db.RequestStatusHistory.AsNoTracking()
        .Where(h => h.RequestId == id)
        .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
        .Select(h => new RequestStatusHistoryDto(
            h.FromStatus, h.ToStatus, h.ChangedBy != null ? h.ChangedBy.FullName : null, h.Reason, h.ChangedAt));

    private async Task<BookingRequestDetailDto?> LoadDetailAsync(long id, CancellationToken ct)
    {
        var row = await db.BookingRequests.AsNoTracking().Where(r => r.Id == id).Select(r => new
        {
            r.Id, r.Purpose, r.Status, r.Attendees, r.RequestedStart, r.RequestedEnd, r.BudgetLkr, r.Notes,
            Requester = new RequesterDto(r.RequesterId, r.Requester.FullName, r.Requester.Email),
            Club = r.Club != null ? new ClubRefDto(r.Club.Id, r.Club.Name) : null,
            r.RequiredFeatures,
            Equipment = r.EquipmentLines.OrderBy(l => l.Type.Code)
                .Select(l => new RequestedEquipmentDto(l.TypeId, l.Type.Code, l.Type.Name, l.Quantity)).ToList(),
            r.CreatedAt, r.UpdatedAt,
        }).SingleOrDefaultAsync(ct);
        if (row is null)
            return null;

        var names = await db.Features.AsNoTracking().Where(f => row.RequiredFeatures.Contains(f.Code))
            .ToDictionaryAsync(f => f.Code, f => f.Name, ct);
        var history = await HistoryQuery(id).ToListAsync(ct);
        return new BookingRequestDetailDto(
            row.Id, row.Purpose, row.Status, row.Attendees, row.RequestedStart, row.RequestedEnd, row.BudgetLkr, row.Notes,
            row.Requester, row.Club,
            // Feature codes can't change and a feature in use can't be deleted, so every code has a name.
            row.RequiredFeatures.Select(code => new RequiredFeatureDto(code, names.GetValueOrDefault(code, code))).ToList(),
            row.Equipment, history, LatestProposal: null, row.CreatedAt, row.UpdatedAt);
    }

    /// <summary>Normalised like FeatureService (trimmed, lower-case), de-duplicated in order. Unknown codes are listed.</summary>
    private async Task<List<string>> CheckFeaturesAsync(string[]? requested, Dictionary<string, List<string>> errors, CancellationToken ct)
    {
        var codes = (requested ?? []).Select(c => (c ?? "").Trim().ToLowerInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        if (codes.Count == 0)
            return [];

        var known = await db.Features.Where(f => codes.Contains(f.Code)).Select(f => f.Code).ToListAsync(ct);
        var unknown = codes.Except(known).ToList();
        if (unknown.Count > 0)
            AddError(errors, "RequiredFeatures", $"Unknown feature codes: {string.Join(", ", unknown)}.");
        return codes;
    }

    private async Task<List<RequestedEquipmentLine>> CheckEquipmentAsync(
        List<RequestedEquipmentLineRequest>? requested, Dictionary<string, List<string>> errors, CancellationToken ct)
    {
        var lines = requested ?? [];
        if (lines.Count == 0)
            return [];

        var duplicates = lines.GroupBy(l => l.TypeId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            AddError(errors, "Equipment", $"Each equipment type can appear only once (repeated: {string.Join(", ", duplicates)}).");

        var typeIds = lines.Select(l => l.TypeId).Distinct().ToList();
        var known = await db.EquipmentTypes.Where(t => typeIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
        var unknown = typeIds.Except(known).ToList();
        if (unknown.Count > 0)
            AddError(errors, "Equipment", $"Unknown equipment types: {string.Join(", ", unknown)}.");

        return lines.Select(l => new RequestedEquipmentLine { TypeId = l.TypeId, Quantity = l.Quantity }).ToList();
    }

    /// <summary>V11: a Student books for an active club they represent; a Lecturer's booking is academic (no club).</summary>
    private async Task CheckClubAsync(long requesterId, long? clubId, Dictionary<string, List<string>> errors, CancellationToken ct)
    {
        if (currentUser.IsInRole(Roles.Lecturer))
        {
            if (clubId is not null)
                AddError(errors, "ClubId", LecturerClubMessage);
            return;
        }

        var isRepresentative = clubId is { } id && await db.ClubMembers.AnyAsync(
            m => m.ClubId == id && m.UserId == requesterId && m.IsRepresentative && m.Club.IsActive, ct);
        if (!isRepresentative)
            AddError(errors, "ClubId", NotRepresentativeMessage);
    }

    private static void AddError(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
            errors[field] = messages = [];
        messages.Add(message);
    }
}
