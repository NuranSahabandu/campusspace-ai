using System.Net;
using CampusSpace.Api.Data;
using CampusSpace.Api.Extensions;
using CampusSpace.Api.Models;
using CampusSpace.Api.Photos;
using CampusSpace.Api.Services;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static CampusSpace.Tests.Infrastructure.LoanTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// POST /api/loans/{id}/checkin (UC11) and GET /api/loans/{id}/photo. Each test checks out its own item from its own
/// booking. Photos go to the factory's own temporary folder (DamagePhotosPath).
/// </summary>
[Collection(PostgresCollection.Name)]
public class LoansCheckInTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = CampusTime.At(new DateOnly(2031, 3, 12), new TimeOnly(10, 0));

    private CustomWebApplicationFactory Factory => fixture.Factory;

    /// <summary>A new booking starting in 10 minutes, one item checked out by a new technician.</summary>
    private static async Task<(HttpClient Tech, long LoanId, long ItemId)> OnLoanAsync(CustomWebApplicationFactory factory, DateTimeOffset? now = null)
    {
        var start = (now ?? DateTimeOffset.UtcNow).AddMinutes(10);
        var h = await HandoverAsync(factory, start, start.AddHours(2));
        var (tech, _) = await TechnicianAsync(factory);
        return (tech, await CheckoutOkAsync(tech, h.BookingId, h.ItemIds[0]), h.ItemIds[0]);
    }

    private static async Task<Dictionary<string, string>> FieldErrorsAsync(HttpResponseMessage response) =>
        (await response.ShouldBeProblemAsync(400)).GetProperty("errors").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value[0].GetString()!);

    private static string[] StoredPhotos(CustomWebApplicationFactory factory) =>
        Directory.Exists(factory.DamagePhotosPath) ? Directory.GetFiles(factory.DamagePhotosPath) : [];

    [Theory]
    [InlineData(EquipmentConditions.Good)]
    [InlineData(EquipmentConditions.MinorWear)]
    public async Task A_good_or_worn_return_makes_the_item_available_with_that_condition(string condition)
    {
        var (tech, loanId, itemId) = await OnLoanAsync(Factory);

        var response = await CheckInAsync(tech, loanId, condition);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("returnCondition").GetString().Should().Be(condition);
        body.GetProperty("isLateReturn").GetBoolean().Should().BeFalse();
        body.GetProperty("hasPhoto").GetBoolean().Should().BeFalse();
        var item = await ItemAsync(Factory, itemId);
        item.Status.Should().Be(EquipmentItemStatuses.Available);
        item.Condition.Should().Be(condition);
        var loan = await LoanAsync(Factory, loanId);
        loan.CheckedInAt.Should().NotBeNull();
        loan.CheckedInById.Should().NotBeNull();
    }

    [Fact]
    public async Task A_damaged_return_with_note_and_photo_sends_the_item_to_repair_and_the_photo_is_served()
    {
        var (tech, loanId, itemId) = await OnLoanAsync(Factory);

        var response = await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "  Cracked grille  ", PngBytes, "evidence.jpg");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.ReadJsonAsync();
        body.GetProperty("damageNote").GetString().Should().Be("Cracked grille");
        body.GetProperty("hasPhoto").GetBoolean().Should().BeTrue();
        var item = await ItemAsync(Factory, itemId);
        item.Status.Should().Be(EquipmentItemStatuses.UnderRepair);
        item.Condition.Should().Be(EquipmentConditions.Damaged);
        // The name is random and the extension comes from the magic bytes, not from "evidence.jpg".
        var loan = await LoanAsync(Factory, loanId);
        loan.DamagePhotoKey.Should().MatchRegex("^[0-9a-f]{32}\\.png$");
        loan.DamagePhotoContentType.Should().Be("image/png");
        loan.DamagePhotoSizeBytes.Should().Be(PngBytes.Length);

        var (officer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);
        foreach (var client in new[] { tech, officer })
        {
            var photo = await client.GetAsync(PhotoUrl(loanId));
            photo.StatusCode.Should().Be(HttpStatusCode.OK);
            photo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
            photo.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
            photo.Headers.CacheControl!.ToString().Should().Be("no-store, private");
            photo.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
            (await photo.Content.ReadAsByteArrayAsync()).Should().Equal(PngBytes);
        }
    }

    [Fact]
    public async Task A_jpeg_is_served_as_image_jpeg()
    {
        var (tech, loanId, _) = await OnLoanAsync(Factory);
        (await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Dented", JpegBytes, "x.png", "image/png"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var photo = await tech.GetAsync(PhotoUrl(loanId));

        photo.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task Damaged_without_a_photo_or_a_note_is_a_400_and_changes_nothing()
    {
        var (tech, loanId, itemId) = await OnLoanAsync(Factory);

        var noPhoto = await FieldErrorsAsync(await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Cracked"));
        var noNote = await FieldErrorsAsync(await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "   ", PngBytes));
        var neither = await FieldErrorsAsync(await CheckInAsync(tech, loanId, EquipmentConditions.Damaged));

        noPhoto.Should().Equal(new Dictionary<string, string> { ["Photo"] = LoanService.DamagedPhotoMessage });
        noNote.Should().Equal(new Dictionary<string, string> { ["Note"] = LoanService.DamagedNoteMessage });
        neither.Keys.Should().BeEquivalentTo("Note", "Photo");
        (await LoanAsync(Factory, loanId)).CheckedInAt.Should().BeNull();
        (await ItemAsync(Factory, itemId)).Status.Should().Be(EquipmentItemStatuses.OnLoan);
    }

    [Fact]
    public async Task A_text_file_named_jpg_is_rejected_by_its_magic_bytes()
    {
        var (tech, loanId, _) = await OnLoanAsync(Factory);

        var errors = await FieldErrorsAsync(await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Cracked",
            "this is not an image"u8.ToArray(), "photo.jpg", "image/jpeg"));

        errors.Should().Equal(new Dictionary<string, string> { ["Photo"] = DamagePhotoRules.NotAnImageMessage });
        (await LoanAsync(Factory, loanId)).CheckedInAt.Should().BeNull();
    }

    [Fact]
    public async Task A_photo_over_5_MB_is_rejected()
    {
        var (tech, loanId, _) = await OnLoanAsync(Factory);
        var big = new byte[DamagePhotoRules.MaxBytes + 1];
        PngBytes.CopyTo(big, 0);

        var errors = await FieldErrorsAsync(await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Cracked", big));

        errors.Should().Equal(new Dictionary<string, string> { ["Photo"] = DamagePhotoRules.TooLargeMessage });
    }

    [Fact]
    public async Task A_body_over_the_6_MB_form_limit_is_refused_without_changing_anything()
    {
        var (tech, loanId, itemId) = await OnLoanAsync(Factory);
        var huge = new byte[7 * 1024 * 1024];
        PngBytes.CopyTo(huge, 0);

        var response = await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Cracked", huge);

        (await response.ShouldBeProblemAsync(400)).GetProperty("errors").ToString()
            .Should().Contain("Failed to read the request form"); // Kestrel: body size limit; TestServer: multipart limit
        (await LoanAsync(Factory, loanId)).CheckedInAt.Should().BeNull();
        (await ItemAsync(Factory, itemId)).Status.Should().Be(EquipmentItemStatuses.OnLoan);
        (await LoanAsync(Factory, loanId)).DamagePhotoKey.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_condition_is_a_400()
    {
        var (tech, loanId, _) = await OnLoanAsync(Factory);

        (await FieldErrorsAsync(await CheckInAsync(tech, loanId, "Lost"))).Should().ContainKey("Condition");
    }

    [Fact]
    public async Task A_loan_already_checked_in_is_a_409_and_an_unknown_loan_a_404()
    {
        var (tech, loanId, _) = await OnLoanAsync(Factory);
        (await CheckInAsync(tech, loanId, EquipmentConditions.Good)).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Cracked", PngBytes);

        (await again.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be(LoanService.AlreadyCheckedInMessage);
        (await CheckInAsync(tech, long.MaxValue / 2, EquipmentConditions.Good)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // The refused check-in wrote no file.
        (await LoanAsync(Factory, loanId)).DamagePhotoKey.Should().BeNull();
    }

    [Fact]
    public async Task A_return_after_the_due_time_is_flagged_late()
    {
        // Checked out at 10:00 for a booking that ended at 11:00; checked in at 12:00.
        var dueAt = Now.AddHours(1);
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Now.AddHours(2)));
        var h = await HandoverAsync(factory, Now.AddMinutes(-30), dueAt);
        var (tech, techId) = await TechnicianAsync(factory);
        var loanId = await InsertLoanAsync(factory, h.BookingId, h.ItemIds[0], techId, Now, dueAt);

        var body = await (await CheckInAsync(tech, loanId, EquipmentConditions.Good)).ReadJsonAsync();

        body.GetProperty("isLateReturn").GetBoolean().Should().BeTrue();
        (await LoanAsync(factory, loanId)).CheckedInAt.Should().Be(Now.AddHours(2).UtcDateTime);
    }

    [Fact]
    public async Task A_return_exactly_at_the_due_time_is_not_late()
    {
        var dueAt = Now.AddHours(1);
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(dueAt));
        var h = await HandoverAsync(factory, Now.AddMinutes(-30), dueAt);
        var (tech, techId) = await TechnicianAsync(factory);
        var loanId = await InsertLoanAsync(factory, h.BookingId, h.ItemIds[0], techId, Now, dueAt);

        var body = await (await CheckInAsync(tech, loanId, EquipmentConditions.Good)).ReadJsonAsync();

        body.GetProperty("isLateReturn").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_photo_file_is_removed_when_the_save_fails()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync(new FixedTimeProvider(Now));
        var (tech, loanId, itemId) = await OnLoanAsync(factory, Now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Only this test's database: every check-in UPDATE now fails after the photo has been written.
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_checkin() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'simulated failure'; END $$;
                CREATE TRIGGER fail_checkin BEFORE UPDATE ON "EquipmentLoans" FOR EACH ROW EXECUTE FUNCTION fail_checkin();
                """);
        }

        var response = await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Cracked", PngBytes);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        StoredPhotos(factory).Should().BeEmpty();
        (await LoanAsync(factory, loanId)).CheckedInAt.Should().BeNull();
        (await ItemAsync(factory, itemId)).Status.Should().Be(EquipmentItemStatuses.OnLoan);
    }

    [Fact]
    public async Task Only_lab_technicians_check_in()
    {
        var (_, loanId, _) = await OnLoanAsync(Factory);
        var (officer, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.FacilitiesOfficer);
        var (student, _) = await TestAuth.CreateUserClientAsync(Factory, Roles.Student);

        (await CheckInAsync(officer, loanId, EquipmentConditions.Good)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CheckInAsync(student, loanId, EquipmentConditions.Good)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CheckInAsync(Factory.CreateClient(), loanId, EquipmentConditions.Good)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_photo_endpoint_is_staff_only_and_404_without_a_photo()
    {
        var (tech, loanId, _) = await OnLoanAsync(Factory);

        (await tech.GetAsync(PhotoUrl(loanId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Cracked", JpegBytes)).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var role in new[] { Roles.Student, Roles.Lecturer, Roles.Admin })
            (await TestAuth.CreateClient(Factory, role).GetAsync(PhotoUrl(loanId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Factory.CreateClient().GetAsync(PhotoUrl(loanId))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await tech.GetAsync(PhotoUrl(long.MaxValue / 2))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- The photo store (FakePhotoStore: in memory, no network) ----

    [Fact]
    public async Task The_photo_goes_to_the_active_store_and_comes_back_byte_for_byte_with_the_stored_type()
    {
        var store = new FakePhotoStore();
        await using var api = Factory.WithPhotoStore(store);
        var (tech, loanId, _) = await OnLoanAsync(Factory);
        var client = api.SameUser(tech);
        var photoBytes = JpegBytes.Concat(Enumerable.Range(0, 4000).Select(i => (byte)i)).ToArray();

        (await CheckInAsync(client, loanId, EquipmentConditions.Damaged, "Bent", photoBytes, "a.png", "image/png"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var loan = await LoanAsync(Factory, loanId);
        store.Objects.Should().ContainSingle().Which.Key.Should().Be(loan.DamagePhotoKey);
        store.Objects[loan.DamagePhotoKey!].ContentType.Should().Be("image/jpeg");
        loan.DamagePhotoSizeBytes.Should().Be(photoBytes.Length);
        StoredPhotos(Factory).Should().NotContain(f => f.EndsWith(loan.DamagePhotoKey!)); // not the local folder
        var photo = await client.GetAsync(PhotoUrl(loanId));
        photo.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await photo.Content.ReadAsByteArrayAsync()).Should().Equal(photoBytes);
    }

    [Fact]
    public async Task A_photo_whose_object_is_gone_is_a_404()
    {
        var store = new FakePhotoStore();
        await using var api = Factory.WithPhotoStore(store);
        var (tech, loanId, _) = await OnLoanAsync(Factory);
        var client = api.SameUser(tech);
        (await CheckInAsync(client, loanId, EquipmentConditions.Damaged, "Bent", PngBytes)).StatusCode.Should().Be(HttpStatusCode.OK);

        store.Objects.Clear();

        (await client.GetAsync(PhotoUrl(loanId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(PhotoStoreFailure.Unreachable, 503, PhotoStoreUnavailableException.UnreachableMessage)]
    [InlineData(PhotoStoreFailure.Failed, 502, PhotoStoreUnavailableException.FailedMessage)]
    public async Task A_store_failure_on_read_is_a_clean_problem(PhotoStoreFailure failure, int status, string title)
    {
        var store = new FakePhotoStore();
        await using var api = Factory.WithPhotoStore(store);
        var (tech, loanId, _) = await OnLoanAsync(Factory);
        var client = api.SameUser(tech);
        (await CheckInAsync(client, loanId, EquipmentConditions.Damaged, "Bent", PngBytes)).StatusCode.Should().Be(HttpStatusCode.OK);

        store.FailWith = failure;
        var response = await client.GetAsync(PhotoUrl(loanId));

        var problem = await response.ShouldBeProblemAsync(status);
        problem.GetProperty("title").GetString().Should().Be(title);
        problem.TryGetProperty("detail", out _).Should().BeFalse();
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_store_failure_on_check_in_is_a_503_and_changes_nothing()
    {
        var store = new FakePhotoStore { FailWith = PhotoStoreFailure.Unreachable };
        await using var api = Factory.WithPhotoStore(store);
        var (tech, loanId, itemId) = await OnLoanAsync(Factory);

        var response = await CheckInAsync(api.SameUser(tech), loanId, EquipmentConditions.Damaged, "Bent", PngBytes);

        (await response.ShouldBeProblemAsync(503)).GetProperty("title").GetString()
            .Should().Be(PhotoStoreUnavailableException.UnreachableMessage);
        var loan = await LoanAsync(Factory, loanId);
        loan.CheckedInAt.Should().BeNull();
        loan.DamagePhotoKey.Should().BeNull();
        (await ItemAsync(Factory, itemId)).Status.Should().Be(EquipmentItemStatuses.OnLoan);
    }

    [Fact]
    public async Task A_rule_failing_under_the_lock_deletes_the_uploaded_object()
    {
        // The pre-check passes; while the photo is "uploading", another technician checks the same loan in. The first
        // request then finds the loan closed under its row lock: 409, and its uploaded object is removed.
        var store = new FakePhotoStore();
        await using var api = Factory.WithPhotoStore(store);
        var (tech, loanId, itemId) = await OnLoanAsync(Factory);
        var client = api.SameUser(tech);
        var uploaded = new List<string>();
        store.DuringPut = async key =>
        {
            uploaded.Add(key);
            (await CheckInAsync(client, loanId, EquipmentConditions.Good)).StatusCode.Should().Be(HttpStatusCode.OK);
        };

        var response = await CheckInAsync(client, loanId, EquipmentConditions.Damaged, "Bent", PngBytes);

        (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be(LoanService.AlreadyCheckedInMessage);
        uploaded.Should().ContainSingle();
        store.Objects.Should().BeEmpty();
        var loan = await LoanAsync(Factory, loanId);
        loan.ReturnCondition.Should().Be(EquipmentConditions.Good);
        loan.DamagePhotoKey.Should().BeNull();
        (await ItemAsync(Factory, itemId)).Status.Should().Be(EquipmentItemStatuses.Available);
    }

    [Fact]
    public async Task If_the_cleanup_delete_also_fails_the_request_still_answers_and_the_orphan_is_left()
    {
        var store = new FakePhotoStore { FailDelete = true };
        await using var api = Factory.WithPhotoStore(store);
        var (tech, loanId, _) = await OnLoanAsync(Factory);
        var client = api.SameUser(tech);
        store.DuringPut = async _ => await CheckInAsync(client, loanId, EquipmentConditions.Good);

        var response = await CheckInAsync(client, loanId, EquipmentConditions.Damaged, "Bent", PngBytes);

        // The original 409 is what the client sees; the orphan (private, unreferenced, random key) is only logged.
        (await response.ShouldBeProblemAsync(409)).GetProperty("title").GetString().Should().Be(LoanService.AlreadyCheckedInMessage);
        store.Objects.Should().ContainSingle();
    }

    [Fact]
    public async Task A_check_in_on_a_closed_or_unknown_loan_uploads_nothing()
    {
        var store = new FakePhotoStore();
        await using var api = Factory.WithPhotoStore(store);
        var (tech, loanId, _) = await OnLoanAsync(Factory);
        var client = api.SameUser(tech);
        (await CheckInAsync(client, loanId, EquipmentConditions.Good)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await CheckInAsync(client, loanId, EquipmentConditions.Damaged, "Bent", PngBytes)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CheckInAsync(client, long.MaxValue / 2, EquipmentConditions.Damaged, "Bent", PngBytes)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        store.Objects.Should().BeEmpty();
    }

    [Fact]
    public async Task A_slow_store_holds_no_loan_or_item_lock()
    {
        var store = new FakePhotoStore();
        await using var api = Factory.WithPhotoStore(store);
        var (tech, loanId, itemId) = await OnLoanAsync(Factory);
        var uploading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.DuringPut = async _ =>
        {
            uploading.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
        };

        var checkIn = CheckInAsync(api.SameUser(tech), loanId, EquipmentConditions.Damaged, "Bent", PngBytes);
        await uploading.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // NOWAIT fails at once (55P03) if anyone holds the row lock. Both rows are free while the upload is in flight.
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (var (table, id) in new[] { ("EquipmentLoans", loanId), ("EquipmentItems", itemId) })
            {
                await using var command = new NpgsqlCommand($"""SELECT 1 FROM "{table}" WHERE "Id" = @id FOR UPDATE NOWAIT""", connection, transaction);
                command.Parameters.AddWithValue("id", id);
                (await command.ExecuteScalarAsync()).Should().Be(1);
            }
            await transaction.RollbackAsync();
        }
        release.SetResult();

        (await checkIn).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoanAsync(Factory, loanId)).DamagePhotoKey.Should().NotBeNull();
    }
}
