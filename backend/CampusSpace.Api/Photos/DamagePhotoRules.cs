using CampusSpace.Api.Middleware;

namespace CampusSpace.Api.Photos;

/// <summary>An image type a damage photo may have, found from the file's magic bytes.</summary>
public sealed record PhotoFormat(string Extension, string ContentType);

/// <summary>
/// The damage-photo upload rules (plan §15.3): JPEG or PNG only, checked by magic bytes; at most <see cref="MaxBytes"/>.
/// The same for every <see cref="IPhotoStore"/>; mobile mirrors them in CheckInRules.
/// </summary>
public static class DamagePhotoRules
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

    /// <summary>The number of leading bytes <see cref="Detect"/> needs.</summary>
    public static int HeaderLength => PngMagic.Length;

    /// <summary>The image type from the first bytes of a file, or null. Content-Type and file name are never trusted.</summary>
    public static PhotoFormat? Detect(ReadOnlySpan<byte> header) =>
        header.StartsWith(PngMagic) ? Png : header.StartsWith(JpegMagic) ? Jpeg : null;

    /// <summary>Checks size and magic bytes. Throws BusinessRuleException (400 on Photo) if the file is not acceptable.</summary>
    public static async Task<PhotoFormat> ValidateAsync(IFormFile photo, CancellationToken ct = default)
    {
        if (photo.Length == 0)
            throw new BusinessRuleException(Field, EmptyMessage);
        if (photo.Length > MaxBytes)
            throw new BusinessRuleException(Field, TooLargeMessage);

        await using var stream = photo.OpenReadStream();
        return await DetectAsync(stream, ct) ?? throw new BusinessRuleException(Field, NotAnImageMessage);
    }

    /// <summary>Reads the first bytes of a stream and detects the image type.</summary>
    public static async Task<PhotoFormat?> DetectAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[HeaderLength];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        return Detect(header.AsSpan(0, read));
    }
}
