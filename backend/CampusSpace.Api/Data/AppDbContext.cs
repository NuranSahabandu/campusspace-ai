using System.Globalization;
using System.Text.Json;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CampusSpace.Api.Data;

/// <param name="currentUser">Who is making the change, for AuditLogs.UserId. Null outside a request (seeding, tests).</param>
public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<ClubMember> ClubMembers => Set<ClubMember>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomFeature> RoomFeatures => Set<RoomFeature>();
    public DbSet<RoomBlackout> RoomBlackouts => Set<RoomBlackout>();
    public DbSet<EquipmentType> EquipmentTypes => Set<EquipmentType>();
    public DbSet<EquipmentItem> EquipmentItems => Set<EquipmentItem>();
    public DbSet<EquipmentSubstitute> EquipmentSubstitutes => Set<EquipmentSubstitute>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<PolicySetting> PolicySettings => Set<PolicySetting>();
    public DbSet<BookingRequest> BookingRequests => Set<BookingRequest>();
    public DbSet<RequestedEquipmentLine> RequestedEquipmentLines => Set<RequestedEquipmentLine>();
    public DbSet<RequestStatusHistory> RequestStatusHistory => Set<RequestStatusHistory>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<EquipmentReservation> EquipmentReservations => Set<EquipmentReservation>();
    public DbSet<EquipmentLoan> EquipmentLoans => Set<EquipmentLoan>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationLine> QuotationLines => Set<QuotationLine>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();
    public DbSet<AgentToolCall> AgentToolCalls => Set<AgentToolCall>();
    public DbSet<AgentValidationResult> ValidationResults => Set<AgentValidationResult>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // GiST indexes that mix a scalar column with a range (RoomBlackouts now, the Bookings exclusion constraint later).
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTimestamps();
        var pending = CaptureAuditEntries();
        if (pending.Count == 0)
            return base.SaveChanges(acceptAllChangesOnSuccess);

        using var transaction = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        var result = base.SaveChanges(acceptAllChangesOnSuccess: true);
        AuditLogs.AddRange(BuildAuditLogs(pending));
        base.SaveChanges(acceptAllChangesOnSuccess: true);
        transaction?.Commit();
        return result;
    }

    /// <summary>
    /// Saves, then writes one AuditLogs row per changed IAuditable entity, both in one transaction
    /// (an existing transaction is reused, and the caller commits it). Two saves are needed because
    /// generated keys are only known after the first one. Always accepts changes on success, so the
    /// second save cannot write the entities again.
    /// ExecuteUpdate/ExecuteDelete bypass the change tracker and are NOT audited.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        var pending = CaptureAuditEntries();
        if (pending.Count == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        await using var transaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
        AuditLogs.AddRange(BuildAuditLogs(pending));
        await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>Never named in DetailsJson. Timestamps change on every save, so they would only add noise.</summary>
    private static readonly HashSet<string> UnauditedProperties =
        [nameof(User.PasswordHash), nameof(ITimestamped.CreatedAt), nameof(ITimestamped.UpdatedAt)];

    private sealed record PendingAudit(EntityEntry Entry, string Action, IReadOnlyList<string> Changed, string? KeyBeforeSave);

    /// <summary>Runs before saving: afterwards every entry is Unchanged (or Detached) and IsModified is reset.</summary>
    private List<PendingAudit> CaptureAuditEntries()
    {
        var pending = new List<PendingAudit>();
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            var (action, properties) = entry.State switch
            {
                EntityState.Added => (AuditActions.Created, entry.Properties.Where(p => !p.Metadata.IsPrimaryKey())),
                EntityState.Modified => (AuditActions.Updated, entry.Properties.Where(p => p.IsModified)),
                EntityState.Deleted => (AuditActions.Deleted, []),
                _ => (null, []),
            };
            if (action is null)
                continue;

            var changed = properties
                .Select(p => p.Metadata.Name)
                .Where(name => !UnauditedProperties.Contains(name))
                .ToList();
            // Added keys are generated by the database, so they are read after the first save.
            var key = entry.State == EntityState.Added ? null : KeyOf(entry);
            pending.Add(new PendingAudit(entry, action, changed, key));
        }
        return pending;
    }

    private IEnumerable<AuditLog> BuildAuditLogs(List<PendingAudit> pending)
    {
        var at = DateTime.UtcNow;
        var userId = currentUser?.UserId;
        return pending.Select(p => new AuditLog
        {
            UserId = userId,
            Action = p.Action,
            EntityType = p.Entry.Metadata.ClrType.Name,
            EntityId = p.KeyBeforeSave ?? KeyOf(p.Entry),
            // Names only, never values.
            DetailsJson = p.Action == AuditActions.Deleted ? "{}" : JsonSerializer.Serialize(new { changed = p.Changed }),
            At = at,
        });
    }

    /// <summary>The primary key as text; composite keys are joined with ':' (ClubMember is "ClubId:UserId").</summary>
    private static string KeyOf(EntityEntry entry) => string.Join(':',
        entry.Metadata.FindPrimaryKey()!.Properties.Select(p =>
            Convert.ToString(entry.Property(p.Name).CurrentValue, CultureInfo.InvariantCulture)));

    // Always UTC: Npgsql 8 rejects DateTimeKind.Local for timestamptz columns.
    private void ApplyTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IHasUpdatedAt>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            entry.Entity.UpdatedAt = now;
            if (entry.Entity is not ITimestamped timestamped)
                continue;
            if (entry.State == EntityState.Added)
                timestamped.CreatedAt = now;
            else
                entry.Property(nameof(ITimestamped.CreatedAt)).IsModified = false;
        }
    }
}
