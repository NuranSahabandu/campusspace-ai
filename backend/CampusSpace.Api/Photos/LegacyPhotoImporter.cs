using CampusSpace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CampusSpace.Api.Photos;

public sealed record LegacyImportResult(int Uploaded, int AlreadyPresent, int Skipped, int Failed);

/// <summary>
/// Moves damage photos saved before R2 (files in Storage:DamagePhotosPath) into the active store, once. A file is moved
/// only when a loan references it and its bytes are a valid photo of the type its key says; its key stays the file name,
/// so no row's key changes. Idempotent: an object already in the store is not uploaded again. It also fills
/// DamagePhotoSizeBytes where a legacy row has none. The folder is never deleted: remove it by hand once the logged
/// count looks right (it is git-ignored, and absent on Render).
/// </summary>
public sealed class LegacyPhotoImporter(
    AppDbContext db,
    IPhotoStore store,
    IServiceProvider services,
    ILogger<LegacyPhotoImporter> logger)
{
    public async Task<LegacyImportResult> RunAsync(CancellationToken ct = default)
    {
        var folder = PhotoStorageExtensions.LocalFolder(services);
        if (!Directory.Exists(folder))
            return new LegacyImportResult(0, 0, 0, 0);

        var files = Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().ToList();
        var keys = files.Where(PhotoKeys.IsValid).ToList();
        var loans = await db.EquipmentLoans.Where(l => l.DamagePhotoKey != null && keys.Contains(l.DamagePhotoKey))
            .ToDictionaryAsync(l => l.DamagePhotoKey!, ct);

        int uploaded = 0, present = 0, skipped = files.Count - keys.Count, failed = 0;
        foreach (var key in keys)
        {
            if (!loans.TryGetValue(key, out var loan))
            {
                skipped++;
                continue;
            }
            try
            {
                var path = Path.Combine(folder, key);
                var length = new FileInfo(path).Length;
                await using var file = File.OpenRead(path);
                var format = await DamagePhotoRules.DetectAsync(file, ct);
                if (length is 0 or > DamagePhotoRules.MaxBytes || format is null || format.ContentType != PhotoKeys.ContentTypeOf(key))
                {
                    skipped++;
                    continue;
                }
                file.Position = 0;
                if (await store.ExistsAsync(key, ct))
                    present++;
                else
                {
                    await store.PutAsync(key, file, length, format.ContentType, ct);
                    uploaded++;
                }
                loan.DamagePhotoContentType ??= format.ContentType;
                loan.DamagePhotoSizeBytes ??= (int)length;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                logger.LogWarning("Legacy photo import: one file failed ({ErrorType})", e.GetType().Name);
            }
        }
        await db.SaveChangesAsync(ct);

        var result = new LegacyImportResult(uploaded, present, skipped, failed);
        logger.LogInformation(
            "Imported {Uploaded} legacy photos into {Store} ({AlreadyPresent} already there, {Skipped} skipped, {Failed} failed); the local folder is left in place",
            uploaded, store.Name, present, skipped, failed);
        return result;
    }
}
