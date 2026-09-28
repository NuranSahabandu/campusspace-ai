using CampusSpace.Api.Agents;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.AgentTools;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Requests;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using CampusSpace.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CampusSpace.Api.Services;

public sealed class BookingRequestService(
    AppDbContext db,
    ICurrentUser currentUser,
    IPolicySettingsService policy,
    IRequestStateMachine stateMachine,
    IBookingWindowRules windowRules,
    IAgentRunStarter runStarter,
    IOptions<AgentServiceOptions> agentOptions,
    TimeProvider clock) : IBookingRequestService
{
    public const string NotRepresentativeMessage = "You must be the registered representative of an active club";
    public const string LecturerClubMessage = "Lecturer bookings are academic and can't name a club";
    public const string CancelOwnMessage = "You can only cancel your own requests";
    public const string OfficerReasonMessage = "A reason is required when an officer cancels a request.";
    public const string ProcessingMessage = "The request is being processed; try again in a moment";
    public const string RevisionMessage = "The request is being revised; try again once the new proposal is ready";
    public const string AgentFailedMessage = "The request is waiting for an officer to retry planning; it can't be cancelled now";
    public const string BookingStartedMessage = "The booking has already started";
    public const string EquipmentOnLoanMessage = "Equipment is still on loan; check it in first";
    public const string BookingNotCancellableMessage = "The booking is no longer cancellable";
    public const string NotRestartableMessage = "Only a failed or not-yet-started request can be (re)started";

    /// <summary>The 409 message for a status the state machine can't move to Cancelled.</summary>
    public static string NotCancellableMessage(string status) => status switch
    {
        RequestStatuses.AgentProcessing => ProcessingMessage,
        RequestStatuses.RevisionRequested => RevisionMessage,
        RequestStatuses.AgentFailed => AgentFailedMessage,
        _ => $"The request is already {status.ToLowerInvariant()}",
    };

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

        // One read of the current policy serves the V05/V06 rules and the open-request cap below.
        var snapshot = await policy.GetAsync(ct);
        var start = request.RequestedStart!.Value;
        var end = request.RequestedEnd!.Value;
        var requesterRole = currentUser.IsInRole(Roles.Lecturer) ? Roles.Lecturer : Roles.Student;
        foreach (var (field, messages) in windowRules.Check(start, end, requesterRole, snapshot).ToFieldErrors())
            AddError(errors, field, messages[0]);

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
        await AdvisoryLocks.LockAsync(db.Database, AdvisoryLocks.RequesterOpenRequests, requesterId, ct);

        var open = await CountOpenAsync(requesterId, ct);
        var max = snapshot.MaxOpenRequests;
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
        // §11 step 2: AgentRun #1 (Queued) and Submitted → AgentProcessing commit with the request, so a request never
        // sits in AgentProcessing without a run. The start below is best-effort: a Queued run is the safety net the
        // poller retries (and the watchdog fails after AgentService:StartTimeoutMinutes).
        stateMachine.Transition(entity, RequestStatuses.AgentProcessing, requesterId);
        var run = await runStarter.AddRunAsync(entity, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await StartInlineAsync(run.Id, entity.Id, ct);
        return (await LoadDetailAsync(entity.Id, ct))!;
    }

    /// <summary>Starts a committed run without holding up the 202: a slow agent service leaves it Queued.</summary>
    private Task StartInlineAsync(Guid runId, long requestId, CancellationToken ct) =>
        runStarter.TryStartAsync(runId, requestId, TimeSpan.FromSeconds(agentOptions.Value.InlineStartTimeoutSeconds), ct);

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
                r.Club != null ? r.Club.Name : null, r.Requester.FullName, r.Requester.Email,
                r.CancelledAt, r.IsLateCancellation, r.CancelledByOfficer, r.CreatedAt))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<BookingRequestDetailDto?> GetAsync(long id, CancellationToken ct = default) =>
        await EnsureCanReadAsync(id, ct) ? await LoadDetailAsync(id, ct) : null;

    public async Task<IReadOnlyList<RequestStatusHistoryDto>?> GetHistoryAsync(long id, CancellationToken ct = default) =>
        await EnsureCanReadAsync(id, ct) ? await HistoryQuery(id).ToListAsync(ct) : null;

    public async Task<BookingRequestDetailDto?> CancelAsync(long id, CancelBookingRequestRequest? request, CancellationToken ct = default)
    {
        var callerId = CallerId;
        var officer = CallerIsOfficer;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entity = await LockForUpdateAsync(id, ct);
        if (entity is null)
            return null;
        if (!officer && entity.RequesterId != callerId)
            throw new ForbiddenException(CancelOwnMessage);

        // Untrusted text: stored in the history row as given (trimmed), never interpreted, logged or echoed.
        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
        if (officer && reason is null)
            throw new BusinessRuleException(nameof(CancelBookingRequestRequest.Reason), OfficerReasonMessage);

        // The status read under the lock is the current one: a concurrent cancel has either committed or waits for us.
        if (!RequestStateMachine.CanTransition(entity.Status, RequestStatuses.Cancelled))
            throw new ConflictException(NotCancellableMessage(entity.Status));

        var now = clock.GetUtcNow().UtcDateTime;
        var late = false;
        if (entity.Status == RequestStatuses.Approved)
        {
            var booking = await db.Bookings
                .FromSql($"""SELECT * FROM "Bookings" WHERE "RequestId" = {id} FOR UPDATE""")
                .SingleOrDefaultAsync(ct);
            if (booking is null || booking.Status != BookingStatuses.Confirmed)
                throw new ConflictException(BookingNotCancellableMessage);
            var start = booking.TimeRange.LowerBound;
            if (now >= start)
                throw new ConflictException(BookingStartedMessage);
            // Checkout locks this booking row too, so no loan can start between this check and the commit.
            if (await db.EquipmentLoans.AnyAsync(l => l.BookingId == booking.Id && l.CheckedInAt == null, ct))
                throw new ConflictException(EquipmentOnLoanMessage);

            // Only the owner can be late (an officer's cancel isn't the requester's fault). The boundary itself is free.
            var freeHours = (await policy.GetAsync(ct)).FreeCancellationHours;
            late = !officer && now > start.AddHours(-freeHours);

            // A cancelled booking leaves no_room_overlap and its equipment reservations stop counting: both cover
            // active bookings only, so the reservation rows are kept as history.
            booking.Status = BookingStatuses.Cancelled;
        }
        else if (entity.Status == RequestStatuses.PendingApproval)
        {
            // TODO(Phase 3): end the paused workflow run (the interrupted LangGraph thread) of this request.
        }

        await QuotationService.VoidLiveAsync(db, id, ct);
        entity.CancelledAt = now;
        entity.IsLateCancellation = late;
        entity.CancelledByOfficer = officer;
        stateMachine.Transition(entity, RequestStatuses.Cancelled, callerId, reason);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await LoadDetailAsync(id, ct);
    }

    public async Task<BookingRequestDetailDto?> RetryAgentAsync(long id, CancellationToken ct = default)
    {
        var officerId = CallerId;
        var requesterId = await db.BookingRequests.Where(r => r.Id == id).Select(r => (long?)r.RequesterId).SingleOrDefaultAsync(ct);
        if (requesterId is not { } ownerId)
            return null;

        Guid runId;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // The submit lock first (it guards the cap), then the request row: the order every writer uses.
            await AdvisoryLocks.LockAsync(db.Database, AdvisoryLocks.RequesterOpenRequests, ownerId, ct);
            var entity = (await LockForUpdateAsync(id, ct))!;
            var restartable = entity.Status == RequestStatuses.AgentFailed
                || (entity.Status == RequestStatuses.Submitted
                    && !await db.AgentRuns.AnyAsync(r => r.RequestId == id && AgentRunStatuses.Active.Contains(r.Status), ct));
            if (!restartable)
                throw new ConflictException(NotRestartableMessage);

            // The request becomes AgentProcessing, which is open: it must fit beside the requester's other open requests.
            var open = await CountOpenAsync(ownerId, ct, exceptRequestId: id);
            var max = (await policy.GetAsync(ct)).MaxOpenRequests;
            if (open >= max)
                throw new ConflictException(RequesterCapMessage(open, max));

            var run = await runStarter.AddRunAsync(entity, ct);
            stateMachine.Transition(entity, RequestStatuses.AgentProcessing, officerId);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            runId = run.Id;
        }

        await StartInlineAsync(runId, id, ct);
        return await LoadDetailAsync(id, ct);
    }

    /// <summary>
    /// Loads the request tracked and row-locked (SELECT … FOR UPDATE) in the caller's transaction, so operations that
    /// change one request's status serialise and each sees the status the previous one committed. Cancel, retry-agent
    /// and the agent run poller take it first (see RowLocks for the full order); the Phase 3 approve and reject must too.
    /// </summary>
    private Task<BookingRequest?> LockForUpdateAsync(long id, CancellationToken ct) => RowLocks.RequestAsync(db, id, ct);

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

    /// <summary>The cap message for the officer who retries someone else's request.</summary>
    public static string RequesterCapMessage(int open, int max) =>
        $"The requester already has {open} open requests (the limit is {max})";

    private static DateTime CampusDayStartUtc(DateOnly date) => CampusTime.StartOf(date).UtcDateTime;

    /// <param name="exceptRequestId">Leave this request out (the agent's context counts the requester's other requests).</param>
    private Task<int> CountOpenAsync(long requesterId, CancellationToken ct, long? exceptRequestId = null) =>
        db.BookingRequests.CountAsync(r => r.RequesterId == requesterId && RequestStatuses.Open.Contains(r.Status)
            && r.Id != exceptRequestId, ct);

    public async Task<AgentRequestContextDto?> GetAgentContextAsync(long id, CancellationToken ct = default)
    {
        // Only what the Supervisor needs: no names, emails or user ids leave this query.
        var row = await db.BookingRequests.AsNoTracking().Where(r => r.Id == id).Select(r => new
        {
            r.Id, r.Status, r.RequesterId, RequesterRole = r.Requester.Role,
            Club = r.Club != null ? new { r.Club.Name, r.Club.IsActive } : null,
            IsRepresentative = r.ClubId != null && db.ClubMembers.Any(
                m => m.ClubId == r.ClubId && m.UserId == r.RequesterId && m.IsRepresentative),
            r.Purpose, r.Attendees, r.RequestedStart, r.RequestedEnd, r.RequiredFeatures, r.BudgetLkr, r.Notes,
            Equipment = r.EquipmentLines.OrderBy(l => l.Type.Code).Select(l => new AgentEquipmentLineDto(l.Type.Code, l.Quantity)).ToList(),
        }).SingleOrDefaultAsync(ct);
        if (row is null)
            return null;

        var open = await CountOpenAsync(row.RequesterId, ct, exceptRequestId: row.Id);
        var max = (await policy.GetAsync(ct)).MaxOpenRequests;
        return new AgentRequestContextDto(
            row.Id, row.Status, row.RequesterRole,
            row.Club is { } club ? new AgentClubContextDto(club.Name, club.IsActive, row.IsRepresentative) : null,
            open, max, row.Purpose, row.Attendees, row.RequestedStart, row.RequestedEnd,
            row.RequiredFeatures, row.Equipment, row.BudgetLkr, row.Notes);
    }

    public async Task<bool> EnsureCanReadAsync(long id, CancellationToken ct = default)
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
            h.FromStatus, h.ToStatus, h.ChangedById, h.ChangedBy != null ? h.ChangedBy.FullName : null, h.Reason, h.ChangedAt));

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
            r.CancelledAt, r.IsLateCancellation, r.CancelledByOfficer, r.CreatedAt, r.UpdatedAt,
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
            row.Equipment, history, LatestProposal: null,
            row.CancelledAt, row.IsLateCancellation, row.CancelledByOfficer, row.CreatedAt, row.UpdatedAt);
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
