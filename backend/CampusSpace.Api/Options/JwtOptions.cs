using System.ComponentModel.DataAnnotations;

namespace CampusSpace.Api.Options;

/// <summary>Bound from the "Jwt" section. The key comes from user-secrets (dev) or environment variables, never appsettings.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinKeyBytes = 32;
    public const string KeyTooShortMessage = "Jwt:Key must be at least 32 bytes. Generate one with: openssl rand -hex 32";

    [Required] public string Key { get; set; } = string.Empty;
    [Required] public string Issuer { get; set; } = string.Empty;
    [Required] public string Audience { get; set; } = string.Empty;

    /// <summary>Access-token lifetime (§15.2 baseline: 120 minutes).</summary>
    [Range(1, 1440)] public int AccessTokenMinutes { get; set; } = 120;
}
