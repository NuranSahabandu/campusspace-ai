using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CampusSpace.Api.Data;

/// <summary>
/// Every PostgreSQL advisory lock the API takes, one namespace each. Locks use the two-int key form
/// pg_advisory_xact_lock(namespace, id), so requester 7 and equipment type 7 are different locks. (The single-bigint
/// form and the two-int form never collide either.) Locks are transaction-scoped: they are released at commit or
/// rollback, so callers must hold an open transaction.
/// </summary>
public static class AdvisoryLocks
{
    /// <summary>Serialises one requester's submits, so the open-request cap is counted and enforced atomically.</summary>
    public const int RequesterOpenRequests = 1;

    /// <summary>Serialises reservations of one equipment type, so two approvals can't over-allocate it.</summary>
    public const int EquipmentType = 2;

    /// <summary>
    /// Waits for, then takes, the transaction-scoped lock (<paramref name="space"/>, <paramref name="key"/>). The key
    /// must fit in an int (the two-int form); ids are far below 2^31, and a larger one throws instead of wrapping.
    /// </summary>
    public static Task LockAsync(DatabaseFacade database, int space, long key, CancellationToken ct = default)
    {
        var id = checked((int)key);
        return database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({space}, {id})", ct);
    }
}
