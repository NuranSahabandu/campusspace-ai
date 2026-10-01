using System.Text.RegularExpressions;

namespace CampusSpace.Api.Photos;

/// <summary>
/// Object keys for damage photos: 32 random hex digits plus .jpg or .png (CK_EquipmentLoans_DamagePhotoKey). A key
/// carries no user data, and a key outside this shape is never passed to a store, so it can't name another object or
/// leave the local folder.
/// </summary>
public static partial class PhotoKeys
{
    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png)$")]
    private static partial Regex Pattern();

    public static string New(PhotoFormat format) => Guid.NewGuid().ToString("N") + format.Extension;

    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key);

    /// <summary>The content type a key's extension stands for (the extension came from the magic bytes).</summary>
    public static string ContentTypeOf(string key) =>
        key.EndsWith(DamagePhotoRules.Png.Extension, StringComparison.Ordinal)
            ? DamagePhotoRules.Png.ContentType
            : DamagePhotoRules.Jpeg.ContentType;

    /// <summary>Throws for a key outside the pattern (a programming error, never user input).</summary>
    public static void EnsureValid(string key)
    {
        if (!IsValid(key))
            throw new ArgumentException("Not a damage photo key.", nameof(key));
    }
}
