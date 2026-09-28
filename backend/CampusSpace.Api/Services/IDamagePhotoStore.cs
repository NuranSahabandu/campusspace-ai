namespace CampusSpace.Api.Services;

/// <summary>An image type the photo store accepts, found from the file's magic bytes.</summary>
public sealed record PhotoFormat(string Extension, string ContentType);

/// <summary>A stored photo opened for reading. The caller disposes the stream (File() does).</summary>
public sealed record StoredPhoto(Stream Content, string ContentType);

/// <summary>
/// Damage photos (plan §15.3): JPEG or PNG only, checked by magic bytes; at most <see cref="DamagePhotoStore.MaxBytes"/>;
/// saved under a random file name in Storage:DamagePhotosPath. The database stores only that file name.
/// </summary>
public interface IDamagePhotoStore
{
    /// <summary>Checks size and magic bytes. Throws BusinessRuleException (400 on Photo) if the file is not acceptable.</summary>
    Task<PhotoFormat> ValidateAsync(IFormFile photo, CancellationToken ct = default);

    /// <summary>Writes a validated photo under a new random name and returns that name.</summary>
    Task<string> SaveAsync(IFormFile photo, PhotoFormat format, CancellationToken ct = default);

    /// <summary>Removes a stored photo if it exists. Never throws for a missing file.</summary>
    void Delete(string name);

    /// <summary>Opens a stored photo, or null if the name is not a stored photo name or the file is gone.</summary>
    StoredPhoto? Open(string name);
}
