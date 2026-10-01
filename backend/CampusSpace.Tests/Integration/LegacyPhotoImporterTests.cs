using CampusSpace.Api.Data;
using CampusSpace.Api.Models;
using CampusSpace.Api.Photos;
using CampusSpace.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CampusSpace.Tests.Infrastructure.LoanTestData;

namespace CampusSpace.Tests.Integration;

/// <summary>
/// LegacyPhotoImporter: photos saved in the local folder before R2 are moved into the active store once (here a
/// FakePhotoStore), keeping their key; unreferenced or invalid files are skipped and the folder is left in place.
/// Own database and folder, so the counts are exact.
/// </summary>
[Collection(PostgresCollection.Name)]
public class LegacyPhotoImporterTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Referenced_photos_are_uploaded_once_and_their_size_filled()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        // Two damaged check-ins through the API with the local store: the "legacy" photos.
        var keys = new List<string>();
        foreach (var bytes in new[] { PngBytes, JpegBytes })
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(10);
            var h = await HandoverAsync(factory, start, start.AddHours(2));
            var (tech, _) = await TechnicianAsync(factory);
            var loanId = await CheckoutOkAsync(tech, h.BookingId, h.ItemIds[0]);
            (await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Cracked", bytes)).EnsureSuccessStatusCode();
            keys.Add((await LoanAsync(factory, loanId)).DamagePhotoKey!);
        }
        // Rows from before the migration had no size.
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().EquipmentLoans
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.DamagePhotoSizeBytes, (int?)null));
        // Files no loan references, or that aren't photo keys, are skipped.
        await File.WriteAllBytesAsync(Path.Combine(factory.DamagePhotosPath, PhotoKeys.New(DamagePhotoRules.Png)), PngBytes);
        await File.WriteAllTextAsync(Path.Combine(factory.DamagePhotosPath, "notes.txt"), "x");

        var store = new FakePhotoStore();
        await using var api = factory.WithPhotoStore(store);

        var first = await RunAsync(api);
        var second = await RunAsync(api);

        first.Should().Be(new LegacyImportResult(Uploaded: 2, AlreadyPresent: 0, Skipped: 2, Failed: 0));
        second.Should().Be(new LegacyImportResult(Uploaded: 0, AlreadyPresent: 2, Skipped: 2, Failed: 0));
        store.Objects.Keys.Should().BeEquivalentTo(keys);
        store.Objects[keys[0]].Bytes.Should().Equal(PngBytes);
        store.Objects[keys[0]].ContentType.Should().Be("image/png");
        store.Objects[keys[1]].ContentType.Should().Be("image/jpeg");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().EquipmentLoans.AsNoTracking()
                .Where(l => l.DamagePhotoKey != null).ToListAsync();
            rows.Select(l => l.DamagePhotoKey).Should().BeEquivalentTo(keys);
            rows.Single(l => l.DamagePhotoKey == keys[0]).DamagePhotoSizeBytes.Should().Be(PngBytes.Length);
            rows.Single(l => l.DamagePhotoKey == keys[1]).DamagePhotoSizeBytes.Should().Be(JpegBytes.Length);
        }
        Directory.GetFiles(factory.DamagePhotosPath).Should().HaveCount(4); // the folder is left in place
    }

    [Fact]
    public async Task A_file_whose_bytes_do_not_match_its_key_is_skipped_and_a_missing_folder_is_nothing_to_do()
    {
        await using var factory = await fixture.CreateIsolatedFactoryAsync();
        var store = new FakePhotoStore();
        await using var api = factory.WithPhotoStore(store);

        (await RunAsync(api)).Should().Be(new LegacyImportResult(0, 0, 0, 0)); // no folder yet

        var start = DateTimeOffset.UtcNow.AddMinutes(10);
        var h = await HandoverAsync(factory, start, start.AddHours(2));
        var (tech, _) = await TechnicianAsync(factory);
        var loanId = await CheckoutOkAsync(tech, h.BookingId, h.ItemIds[0]);
        (await CheckInAsync(tech, loanId, EquipmentConditions.Damaged, "Cracked", PngBytes)).EnsureSuccessStatusCode();
        var key = (await LoanAsync(factory, loanId)).DamagePhotoKey!;
        await File.WriteAllBytesAsync(Path.Combine(factory.DamagePhotosPath, key), JpegBytes); // .png key, JPEG bytes

        (await RunAsync(api)).Should().Be(new LegacyImportResult(0, 0, 1, 0));
        store.Objects.Should().BeEmpty();
    }

    private static async Task<LegacyImportResult> RunAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LegacyPhotoImporter>().RunAsync();
    }
}
