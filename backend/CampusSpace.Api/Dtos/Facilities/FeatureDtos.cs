using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Dtos.Facilities;

public record FeatureDto(long Id, string Code, string Name);

/// <summary>Code is trimmed and lower-cased before saving, and must then be snake_case (sound_system).</summary>
public record FeatureRequest([Required, MaxLength(50)] string Code, [Required, MaxLength(100)] string Name);
