using CampusSpace.Api.Data;
using CampusSpace.Api.Data.Configurations;
using CampusSpace.Api.Middleware;
using CampusSpace.Api.Models;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static CampusSpace.Tests.Infrastructure.QuotationTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// The Quotations and QuotationLines tables (§8.1) and IQuotationService.CreateDraftAsync. Constraint tests use raw SQL,
/// so no application check can hide a missing constraint.
/// </summary>
[Collection(PostgresCollection.Name)]
public class QuotationPersistenceTests(PostgresFixture fixture)
{
    private CustomWebApplicationFactory Factory => fixture.Factory;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<long> InsertQuotationAsync(
        long requestId, decimal subtotal = 100m, decimal discount = 0m, decimal total = 100m,
        string status = QuotationStatuses.Draft, bool exempt = false, string? reason = null)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "Quotations" ("RequestId", "Subtotal", "Discount", "Total", "DiscountReason", "IsExempt", "Status", "CreatedAt", "UpdatedAt")
            VALUES (@request, @subtotal, @discount, @total, @reason, @exempt, @status, now(), now()) RETURNING "Id"
            """, connection);
        command.Parameters.AddWithValue("request", requestId);
        command.Parameters.AddWithValue("subtotal", subtotal);
        command.Parameters.AddWithValue("discount", discount);
        command.Parameters.AddWithValue("total", total);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("exempt", exempt);
        command.Parameters.AddWithValue("status", status);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task InsertLineAsync(long quotationId, string kind, long? typeId, decimal qty, decimal unitPrice, decimal lineTotal)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "QuotationLines" ("QuotationId", "Kind", "EquipmentTypeId", "Description", "Qty", "UnitPrice", "LineTotal", "CreatedAt", "UpdatedAt")
            VALUES (@quotation, @kind, @type, 'x', @qty, @price, @total, now(), now())
            """, connection);
        command.Parameters.AddWithValue("quotation", quotationId);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("type", (object?)typeId ?? DBNull.Value);
        command.Parameters.AddWithValue("qty", qty);
        command.Parameters.AddWithValue("price", unitPrice);
        command.Parameters.AddWithValue("total", lineTotal);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ShouldFailAsync(Func<Task> act, string sqlState, string constraint)
    {
        var error = (await act.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(sqlState);
        error.ConstraintName.Should().Be(constraint);
    }

    private async Task<Quotation> LoadAsync(long id)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Quotations.AsNoTracking().Include(q => q.Lines.OrderBy(l => l.Id)).SingleAsync(q => q.Id == id);
    }

    [Fact]
    public async Task Draft_snapshots_unit_prices()
    {
        var (_, _, requestId) = await RequestAsync(Factory);
        var type = await EquipmentTestData.CreateTypeAsync(Factory, fee: 500m);
        var id = await CreateDraftAsync(Factory, requestId, Quote(type.Id, 500m));

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.EquipmentTypes.Where(t => t.Id == type.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.FeePerBooking, 900m));
        }

        var saved = await LoadAsync(id);
        saved.Status.Should().Be(QuotationStatuses.Draft);
        saved.Lines.Should().HaveCount(2);
        saved.Lines[1].Should().Match<QuotationLine>(l => l.EquipmentTypeId == type.Id && l.UnitPrice == 500m && l.LineTotal == 1000m);
        saved.Total.Should().Be(5500m);
    }

    [Fact]
    public async Task A_second_draft_in_one_transaction_voids_the_first_and_audits_the_void()
    {
        var (_, _, requestId) = await RequestAsync(Factory);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IQuotationService>();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var first = await service.CreateDraftAsync(requestId, Quote());
        await db.SaveChangesAsync();
        var second = await service.CreateDraftAsync(requestId, Quote(exempt: true));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        (await LoadAsync(first.Id)).Status.Should().Be(QuotationStatuses.Void);
        var latest = await LoadAsync(second.Id);
        latest.Should().Match<Quotation>(q => q.Status == QuotationStatuses.Draft && q.IsExempt && q.Total == 0m);

        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Quotation) && a.EntityId == first.Id.ToString() && a.Action == AuditActions.Updated)
            .SingleAsync();
        // Property names only, never values.
        audit.DetailsJson.Should().Contain("\"Status\"").And.NotContain(QuotationStatuses.Void).And.NotContain(QuotationStatuses.Draft);
    }

    [Fact]
    public async Task Rolling_back_leaves_the_first_draft_and_writes_no_audit_row()
    {
        var (_, _, requestId) = await RequestAsync(Factory);
        var firstId = await CreateDraftAsync(Factory, requestId, Quote());

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<IQuotationService>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await service.CreateDraftAsync(requestId, Quote());
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var check = Factory.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await checkDb.Quotations.Where(q => q.RequestId == requestId).Select(q => new { q.Id, q.Status }).ToListAsync())
            .Should().ContainSingle().Which.Should().Be(new { Id = firstId, Status = QuotationStatuses.Draft });
        (await checkDb.AuditLogs.CountAsync(a => a.EntityType == nameof(Quotation) && a.EntityId == firstId.ToString()
            && a.Action == AuditActions.Updated)).Should().Be(0);
    }

    [Fact]
    public async Task CreateDraft_needs_a_transaction_and_refuses_an_issued_quote()
    {
        var (_, _, requestId) = await RequestAsync(Factory);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IQuotationService>();

        await service.Invoking(s => s.CreateDraftAsync(requestId, Quote())).Should().ThrowAsync<InvalidOperationException>();

        await InsertQuotationAsync(requestId, 100m, 0m, 100m, QuotationStatuses.Issued);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await service.Invoking(s => s.CreateDraftAsync(requestId, Quote())).Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task A_twenty_minute_room_line_passes_the_line_total_check()
    {
        var (_, _, requestId) = await RequestAsync(Factory);
        var start = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromMinutes(330));
        var line = QuotationCalculator.Line(QuotationLineKinds.Room, null, "Computer lab T1, 0.33 h @ LKR 1,500",
            QuotationCalculator.Hours(start, start.AddMinutes(20)), 1500m);

        var id = await CreateDraftAsync(Factory, requestId, Result([line]));

        (await LoadAsync(id)).Lines.Single().Should().Match<QuotationLine>(l => l.Qty == 0.33m && l.LineTotal == 495.00m);
    }

    [Fact]
    public async Task Only_one_live_quote_per_request()
    {
        var (_, _, requestId) = await RequestAsync(Factory);
        await InsertQuotationAsync(requestId, status: QuotationStatuses.Void);
        await InsertQuotationAsync(requestId, status: QuotationStatuses.Draft);
        await InsertQuotationAsync(requestId, status: QuotationStatuses.Void);

        await ShouldFailAsync(() => InsertQuotationAsync(requestId, status: QuotationStatuses.Issued),
            PostgresErrorCodes.UniqueViolation, QuotationConfiguration.LiveQuoteIndex);
    }

    [Theory]
    [InlineData(100, 150, -50, false, null, "CK_Quotations_Discount_LE_Subtotal")]
    [InlineData(100, 0, 90, false, null, "CK_Quotations_Total_Equals_Subtotal_Minus_Discount")]
    [InlineData(100, 50, 50, true, "Lecturer exemption (academic use)", "CK_Quotations_Exempt")]
    [InlineData(100, 100, 0, true, null, "CK_Quotations_Exempt")]
    [InlineData(100, 0, 100, false, null, null)]
    public async Task Quotation_checks(int subtotal, int discount, int total, bool exempt, string? reason, string? constraint)
    {
        var (_, _, requestId) = await RequestAsync(Factory);
        Func<Task> act = () => InsertQuotationAsync(requestId, subtotal, discount, total, exempt: exempt, reason: reason);

        if (constraint is null)
            await act.Should().NotThrowAsync();
        else
        {
            await ShouldFailAsync(act, PostgresErrorCodes.CheckViolation, constraint);
        }
    }

    [Fact]
    public async Task A_negative_total_is_rejected()
    {
        var (_, _, requestId) = await RequestAsync(Factory);
        // A negative total always breaks Discount <= Subtotal too; Postgres reports one of them.
        var error = (await FluentActions.Invoking(() => InsertQuotationAsync(requestId, 100m, 150m, -50m))
            .Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.Should().BeOneOf("CK_Quotations_Total", "CK_Quotations_Discount_LE_Subtotal");
    }

    [Fact]
    public async Task Line_checks_restrict_and_cascade()
    {
        var (_, _, requestId) = await RequestAsync(Factory);
        var type = await EquipmentTestData.CreateTypeAsync(Factory);
        var id = await InsertQuotationAsync(requestId);

        await ShouldFailAsync(() => InsertLineAsync(id, QuotationLineKinds.Room, null, 1.5m, 333.33m, 499.99m),
            PostgresErrorCodes.CheckViolation, "CK_QuotationLines_LineTotal");
        await ShouldFailAsync(() => InsertLineAsync(id, QuotationLineKinds.Room, type.Id, 1m, 100m, 100m),
            PostgresErrorCodes.CheckViolation, "CK_QuotationLines_Kind_Type");
        await ShouldFailAsync(() => InsertLineAsync(id, QuotationLineKinds.Equipment, null, 1m, 100m, 100m),
            PostgresErrorCodes.CheckViolation, "CK_QuotationLines_Kind_Type");
        await ShouldFailAsync(() => InsertLineAsync(id, QuotationLineKinds.Room, null, 0m, 100m, 0m),
            PostgresErrorCodes.CheckViolation, "CK_QuotationLines_Qty");
        await ShouldFailAsync(() => InsertLineAsync(id, QuotationLineKinds.Room, null, 1m, -1m, -1m),
            PostgresErrorCodes.CheckViolation, "CK_QuotationLines_UnitPrice");

        await InsertLineAsync(id, QuotationLineKinds.Room, null, 1.5m, 333.33m, 500.00m);
        await InsertLineAsync(id, QuotationLineKinds.Equipment, type.Id, 2m, 500m, 1000m);

        await using var connection = await OpenAsync();
        // RESTRICT: a quoted equipment type and a quoted request can't be deleted.
        await ShouldFailAsync(async () =>
        {
            await using var command = new NpgsqlCommand($"DELETE FROM \"EquipmentTypes\" WHERE \"Id\" = {type.Id}", connection);
            await command.ExecuteNonQueryAsync();
        }, PostgresErrorCodes.ForeignKeyViolation, "FK_QuotationLines_EquipmentTypes_EquipmentTypeId");
        await ShouldFailAsync(async () =>
        {
            // Its status history restricts the delete too, so remove that first.
            await using var command = new NpgsqlCommand(
                $"DELETE FROM \"RequestStatusHistory\" WHERE \"RequestId\" = {requestId}; DELETE FROM \"BookingRequests\" WHERE \"Id\" = {requestId}",
                connection);
            await command.ExecuteNonQueryAsync();
        }, PostgresErrorCodes.ForeignKeyViolation, "FK_Quotations_BookingRequests_RequestId");

        await using (var delete = new NpgsqlCommand($"DELETE FROM \"Quotations\" WHERE \"Id\" = {id}", connection))
            await delete.ExecuteNonQueryAsync();
        await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"QuotationLines\" WHERE \"QuotationId\" = {id}", connection);
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(0);
    }
}
