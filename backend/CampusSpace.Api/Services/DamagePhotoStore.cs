using System.Text.RegularExpressions;
using CampusSpace.Api.Middleware;

namespace CampusSpace.Api.Services;

/// <summary>
/// File-system <see cref="IDamagePhotoStore"/>. Names are 32 hex digits plus .jpg or .png (the same shape as
/// CK_EquipmentLoans_DamagePhotoPath), so a name can never leave <see cref="Root"/>.
/// </summary>
public sealed partial class DamagePhotoStore(string root) : IDamagePhotoStore
{
    public const string Field = "Photo";

    /// <summary>The §15.3 upload limit (a security control, not booking policy).</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    public const string TooLargeMessage = "Photo must be 5 MB or smaller.";
    public const string EmptyMessage = "Photo is empty.";
    public const string NotAnImageMessage = "Photo must be a JPEG or PNG image.";

    public static readonly PhotoFormat Jpeg = new(".jpg", "image/jpeg");
    public static readonly PhotoFormat Png = new(".png", "image/png");

    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png)$")]
    private static partial Regex NamePattern();

    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>The image type from the first bytes of a file, or null. Content-Type and file name are never trusted.</summary>
    public static PhotoFormat? Detect(ReadOnlySpan<byte> header) =>
        header.StartsWith(PngMagic) ? Png : header.StartsWith(JpegMagic) ? Jpeg : null;

    public async Task<PhotoFormat> ValidateAsync(IFormFile photo, CancellationToken ct = default)
    {
        if (photo.Length == 0)
            throw new BusinessRuleException(Field, EmptyMessage);
        if (photo.Length > MaxBytes)
            throw new BusinessRuleException(Field, TooLargeMessage);

        var header = new byte[PngMagic.Length];
        await using var stream = photo.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        return Detect(header.AsSpan(0, read)) ?? throw new BusinessRuleException(Field, NotAnImageMessage);
    }

    public async Task<string> SaveAsync(IFormFile photo, PhotoFormat format, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Root);
        var name = Guid.NewGuid().ToString("N") + format.Extension;
        await using var file = new FileStream(Path.Combine(Root, name), FileMode.CreateNew, FileAccess.Write);
        await photo.CopyToAsync(file, ct);
        return name;
    }

    public void Delete(string name)
    {
        if (PathOf(name) is { } path)
            File.Delete(path);
    }

    public StoredPhoto? Open(string name)
    {
        if (PathOf(name) is not { } path || !File.Exists(path))
            return null;
        var contentType = name.EndsWith(Png.Extension, StringComparison.Ordinal) ? Png.ContentType : Jpeg.ContentType;
        return new StoredPhoto(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), contentType);
    }

    private string? PathOf(string name) => NamePattern().IsMatch(name) ? Path.Combine(Root, name) : null;
}
