using CampusSpace.Api.Dtos.Policy;

namespace CampusSpace.Api.Services;

/// <summary>
/// The only way to read booking policy in .NET (addendum A.1). Reads the database on every call, with no cache, so
/// callers such as the approval re-check always see current values.
/// </summary>
public interface IPolicySettingsService
{
    /// <summary>
    /// The current policy, typed. Throws InvalidOperationException if a key is missing or its value doesn't parse as its
    /// ValueType: that is a server bug (keys come only from migrations and seed), so it becomes a 500 with a traceId.
    /// </summary>
    Task<PolicySnapshot> GetAsync(CancellationToken ct = default);

    /// <summary>Every setting as stored, with who changed it last, in PolicyKeys order.</summary>
    Task<IReadOnlyList<PolicySettingDto>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Merges the submitted values into the stored ones, checks the result as a whole (400 keyed by setting key), then
    /// saves every changed key with one audit row each, in one SaveChanges. Returns the updated list.
    /// </summary>
    Task<IReadOnlyList<PolicySettingDto>> UpdateAsync(PolicySettingsUpdateRequest request, CancellationToken ct = default);
}
