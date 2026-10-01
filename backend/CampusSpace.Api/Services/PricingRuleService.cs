using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Dtos.Common;
using CampusSpace.Api.Dtos.Pricing;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

/// <summary>
/// Pricing rules (§8.1 Component D). Price history is immutable: rules already in effect are read-only, so a quote
/// can always be recomputed from the rules (V09). See <see cref="PricingRule"/> for why.
/// "Today" is the campus date from the injected clock.
/// </summary>
public sealed class PricingRuleService(AppDbContext db, TimeProvider clock) : IPricingRuleService
{
    public const string InEffectMessage = "Rules already in effect can't be changed; add a new rule with a later start date";

    public Task<PagedResult<PricingRuleDto>> ListAsync(PricingRulesQuery query, CancellationToken ct = default)
    {
        var today = CampusTime.Today(clock);
        var rules = db.PricingRules.AsNoTracking();

        if (query.RoomType is { } roomType)
            rules = rules.Where(r => r.RoomType == roomType);
        if (query.RequesterRole is { } role)
            rules = rules.Where(r => r.RequesterRole == role);
        rules = rules.WhereContains(query.Search, r => r.RoomType, r => r.RequesterRole);
        // Superseded: in effect, but a later rule for the same pair is in effect too.
        if (!query.IncludeHistory)
            rules = rules.Where(r => r.ValidFrom > today || !db.PricingRules.Any(o =>
                o.RoomType == r.RoomType && o.RequesterRole == r.RequesterRole && o.ValidFrom > r.ValidFrom && o.ValidFrom <= today));

        rules = query.Sort switch
        {
            "-roomType" => rules.OrderByDescending(r => r.RoomType).ThenBy(r => r.RequesterRole).ThenBy(r => r.ValidFrom),
            "requesterRole" => rules.OrderBy(r => r.RequesterRole).ThenBy(r => r.RoomType).ThenBy(r => r.ValidFrom),
            "-requesterRole" => rules.OrderByDescending(r => r.RequesterRole).ThenBy(r => r.RoomType).ThenBy(r => r.ValidFrom),
            "validFrom" => rules.OrderBy(r => r.ValidFrom).ThenBy(r => r.RoomType).ThenBy(r => r.RequesterRole),
            "-validFrom" => rules.OrderByDescending(r => r.ValidFrom).ThenBy(r => r.RoomType).ThenBy(r => r.RequesterRole),
            "hourlyRate" => rules.OrderBy(r => r.HourlyRate).ThenBy(r => r.RoomType).ThenBy(r => r.ValidFrom),
            "-hourlyRate" => rules.OrderByDescending(r => r.HourlyRate).ThenBy(r => r.RoomType).ThenBy(r => r.ValidFrom),
            _ => rules.OrderBy(r => r.RoomType).ThenBy(r => r.RequesterRole).ThenBy(r => r.ValidFrom),
        };

        return ToDtos(rules, today).ToPagedResultAsync(query, ct);
    }

    public async Task<IReadOnlyList<CurrentPricingRuleDto>> GetCurrentAsync(CancellationToken ct = default)
    {
        var today = CampusTime.Today(clock);
        // At most one row per pair: in effect, with no later rule in effect.
        var current = await db.PricingRules.AsNoTracking()
            .Where(r => r.ValidFrom <= today && !db.PricingRules.Any(o =>
                o.RoomType == r.RoomType && o.RequesterRole == r.RequesterRole && o.ValidFrom > r.ValidFrom && o.ValidFrom <= today))
            .ToDictionaryAsync(r => (r.RoomType, r.RequesterRole), ct);

        return (
            from roomType in RoomTypes.All.Order()
            from role in RequesterRoles.All
            let rule = current.GetValueOrDefault((roomType, role))
            select new CurrentPricingRuleDto(roomType, role, rule?.Id, rule?.HourlyRate, rule?.IsExempt, rule?.ValidFrom)).ToList();
    }

    public Task<PricingRuleDto?> GetAsync(long id, CancellationToken ct = default)
        => ToDtos(db.PricingRules.AsNoTracking().Where(r => r.Id == id), CampusTime.Today(clock)).SingleOrDefaultAsync(ct);

    public async Task<PricingRuleDto> CreateAsync(PricingRuleRequest request, CancellationToken ct = default)
    {
        var rule = new PricingRule();
        await ApplyAsync(rule, request, ct);
        db.PricingRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(rule.Id, ct))!;
    }

    public async Task<PricingRuleDto?> UpdateAsync(long id, PricingRuleRequest request, CancellationToken ct = default)
    {
        var rule = await db.PricingRules.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
            return null;

        EnsureNotInEffect(rule);
        await ApplyAsync(rule, request, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var rule = await db.PricingRules.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
            return false;

        EnsureNotInEffect(rule);
        db.PricingRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// The booking's campus date, not its UTC date: a booking at 00:30 campus time is the previous day in UTC but
    /// is priced by the rules of its campus day. Because rules in effect never change, calling this again later for
    /// the same booking gives the same rule, which is what lets .NET recompute a quote exactly (V09).
    /// </summary>
    public Task<EffectivePricingRule?> GetEffectiveRuleAsync(
        string roomType, string requesterRole, DateTimeOffset bookingStart, CancellationToken ct = default)
    {
        var date = CampusTime.DateOf(bookingStart);
        return db.PricingRules.AsNoTracking()
            .Where(r => r.RoomType == roomType && r.RequesterRole == requesterRole && r.ValidFrom <= date)
            .OrderByDescending(r => r.ValidFrom)
            .Select(r => new EffectivePricingRule(r.Id, r.HourlyRate, r.IsExempt, r.ValidFrom))
            .FirstOrDefaultAsync(ct);
    }

    private IQueryable<PricingRuleDto> ToDtos(IQueryable<PricingRule> rules, DateOnly today) => rules.Select(r => new PricingRuleDto(
        r.Id, r.RoomType, r.RequesterRole, r.HourlyRate, r.IsExempt, r.ValidFrom,
        r.ValidFrom > today ? PricingRuleStatuses.Scheduled
        : db.PricingRules.Any(o => o.RoomType == r.RoomType && o.RequesterRole == r.RequesterRole
            && o.ValidFrom > r.ValidFrom && o.ValidFrom <= today) ? PricingRuleStatuses.Superseded
        : PricingRuleStatuses.Current));

    private void EnsureNotInEffect(PricingRule rule)
    {
        if (rule.ValidFrom <= CampusTime.Today(clock))
            throw new BusinessRuleException(nameof(PricingRuleRequest.ValidFrom), InEffectMessage);
    }

    /// <summary>Checks the request and copies it onto <paramref name="rule"/>. The same rules apply to create and update.</summary>
    private async Task ApplyAsync(PricingRule rule, PricingRuleRequest request, CancellationToken ct)
    {
        var validFrom = request.ValidFrom!.Value;
        var rate = request.HourlyRate!.Value;
        if (validFrom < CampusTime.Today(clock))
            throw new BusinessRuleException(nameof(PricingRuleRequest.ValidFrom), "Start date can't be in the past.");
        // numeric(10,2) would silently round a third decimal place, so reject it instead.
        if (decimal.Round(rate, 2) != rate)
            throw new BusinessRuleException(nameof(PricingRuleRequest.HourlyRate), "Rate can have at most 2 decimal places.");
        if (request.IsExempt && rate != 0)
            throw new BusinessRuleException(nameof(PricingRuleRequest.HourlyRate), "An exempt rule must have a rate of 0.");

        // The unique index still catches races (GlobalExceptionHandler maps it to the same 409).
        if (await db.PricingRules.AnyAsync(r => r.Id != rule.Id && r.RoomType == request.RoomType
                && r.RequesterRole == request.RequesterRole && r.ValidFrom == validFrom, ct))
            throw new ConflictException(PricingRuleConfiguration.DuplicateRuleMessage);

        rule.RoomType = request.RoomType;
        rule.RequesterRole = request.RequesterRole;
        rule.HourlyRate = rate;
        rule.IsExempt = request.IsExempt;
        rule.ValidFrom = validFrom;
    }
}
