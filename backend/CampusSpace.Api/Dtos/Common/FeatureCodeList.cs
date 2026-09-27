namespace CampusSpace.Api.Dtos.Common;

/// <summary>Feature codes as queries take them: a comma-separated list, trimmed, lower-cased and without duplicates.</summary>
public static class FeatureCodeList
{
    public static IReadOnlyList<string> Parse(string? commaSeparated) =>
        Normalize((commaSeparated ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    public static IReadOnlyList<string> Normalize(IEnumerable<string?> codes) =>
        codes.Select(c => (c ?? "").Trim().ToLowerInvariant()).Where(c => c.Length > 0).Distinct().ToList();
}
