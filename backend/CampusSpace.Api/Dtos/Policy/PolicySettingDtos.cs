using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Dtos.Policy;

/// <summary>One setting as stored (addendum A.1). Value is text, parsed by ValueType; opening_hours is JSON text.</summary>
public record PolicySettingDto(
    string Key, string Value, string ValueType, string Description, DateTime UpdatedAt, string? UpdatedByName);

/// <summary>
/// PUT /api/policy-settings (addendum A.2): the keys to change. Keys that are left out keep their values. ValueType and
/// Description are fixed by migrations and seed data, so they are not part of the body.
/// </summary>
public record PolicySettingsUpdateRequest([Required, MinLength(1)] List<PolicySettingValueRequest> Settings);

public record PolicySettingValueRequest([Required] string Key, [Required(AllowEmptyStrings = false)] string Value);
