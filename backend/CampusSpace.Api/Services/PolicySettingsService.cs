using System.Text.Json;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Data;
using CampusSpace.Api.Dtos.Policy;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Services;

public sealed class PolicySettingsService(AppDbContext db, ICurrentUser currentUser) : IPolicySettingsService
{
    public const string AuditEntityType = nameof(PolicySetting);

    // camelCase, the same as AuditService.
    private static readonly JsonSerializerOptions DetailsJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PolicySnapshot> GetAsync(CancellationToken ct = default)
    {
        var rows = await db.PolicySettings.AsNoTracking().ToListAsync(ct);
        var errors = new Dictionary<string, List<string>>();
        foreach (var row in rows.Where(r => PolicyKeys.TypeOf.GetValueOrDefault(r.Key) != r.ValueType))
            errors[row.Key] = [$"ValueType is '{row.ValueType}', expected '{PolicyKeys.TypeOf.GetValueOrDefault(row.Key)}'."];

        var snapshot = errors.Count == 0 ? PolicyRules.Parse(rows.ToDictionary(r => r.Key, r => r.Value), errors) : null;
        if (snapshot is null)
            throw new InvalidOperationException("PolicySettings are invalid: " +
                string.Join("; ", errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}")));
        return snapshot;
    }

    public async Task<IReadOnlyList<PolicySettingDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.PolicySettings.AsNoTracking()
            .Select(s => new PolicySettingDto(s.Key, s.Value, s.ValueType, s.Description, s.UpdatedAt,
                s.UpdatedBy != null ? s.UpdatedBy.FullName : null))
            .ToListAsync(ct);
        return rows.OrderBy(r => IndexOf(r.Key)).ToList();
    }

    public async Task<IReadOnlyList<PolicySettingDto>> UpdateAsync(PolicySettingsUpdateRequest request, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, List<string>>();
        foreach (var key in request.Settings.Select(s => s.Key).Where(k => !PolicyKeys.TypeOf.ContainsKey(k)).Distinct())
            errors[key] = ["Unknown setting."];
        foreach (var key in request.Settings.GroupBy(s => s.Key).Where(g => g.Count() > 1).Select(g => g.Key))
            errors[key] = ["Setting appears more than once."];
        ThrowIfAny(errors);

        var rows = await db.PolicySettings.ToDictionaryAsync(s => s.Key, ct);
        var merged = rows.ToDictionary(r => r.Key, r => r.Value.Value);
        foreach (var setting in request.Settings)
            merged[setting.Key] = setting.Value;

        var snapshot = PolicyRules.Parse(merged, errors);
        if (snapshot is not null)
            PolicyRules.Validate(snapshot, errors);
        ThrowIfAny(errors);

        // One audit row per changed key, with the old and new value (addendum A.1). PolicySetting is not IAuditable,
        // so AppDbContext writes no names-only row as well. Everything is saved in one SaveChanges, one transaction.
        var at = DateTime.UtcNow;
        foreach (var (key, value) in PolicyRules.ToValues(snapshot!))
        {
            // A missing row would already have failed Parse, so every key is here.
            var row = rows[key];
            if (row.Value == value)
                continue;

            db.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId,
                Action = AuditActions.Updated,
                EntityType = AuditEntityType,
                EntityId = key,
                DetailsJson = JsonSerializer.Serialize(new { key, old = row.Value, @new = value }, DetailsJsonOptions),
                At = at,
            });
            row.Value = value;
            row.UpdatedById = currentUser.UserId;
        }
        await db.SaveChangesAsync(ct);
        return await ListAsync(ct);
    }

    private static int IndexOf(string key) => PolicyKeys.All.ToList().IndexOf(key);

    private static void ThrowIfAny(Dictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
            throw new BusinessRuleException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
