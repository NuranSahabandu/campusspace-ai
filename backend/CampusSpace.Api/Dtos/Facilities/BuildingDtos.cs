using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Dtos.Facilities;

public record BuildingDto(long Id, string Code, string Name, bool IsActive);

/// <summary>Code is trimmed and upper-cased before saving.</summary>
public record CreateBuildingRequest([Required, MaxLength(10)] string Code, [Required, MaxLength(100)] string Name);

public record UpdateBuildingRequest(
    [Required, MaxLength(10)] string Code, [Required, MaxLength(100)] string Name, [Required] bool? IsActive);
