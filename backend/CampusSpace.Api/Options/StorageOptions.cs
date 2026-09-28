using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Options;

/// <summary>
/// Bound from the "Storage" section. DamagePhotosPath is a folder outside any web root (the API serves no static files);
/// a relative path is resolved against the content root. Photos are served only through GET /api/loans/{id}/photo.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required] public string DamagePhotosPath { get; set; } = string.Empty;
}
